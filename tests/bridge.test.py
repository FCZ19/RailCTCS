import datetime, json, os, pathlib, shutil, subprocess, tempfile, threading, time, urllib.request, urllib.error
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

product=pathlib.Path(__file__).resolve().parents[1]
root=product
(root/'work').mkdir(exist_ok=True)
scratch=pathlib.Path(tempfile.mkdtemp(prefix='bridge-check-',dir=root/'work')).resolve()
assert scratch.is_relative_to((root/'work').resolve())
shutil.copy2(os.environ.get('DMI_TEST_EXE',product/'DMI-Web.exe'),scratch/'DMI-Web.exe')
shutil.copytree(product/'dist',scratch/'dist')
(scratch/'bridge-settings.json').write_text(json.dumps({'AutoConnect':False}))
mode='valid';brake_value=.5;control_model=False;vehicle='RVM_BR_Class170_Test_C';calls=[];secret='local-test-key-not-a-game-secret'
speed_ms=25;cab_active=False;cab_ms=0;signal_aspect='Stop';interaction=[];mismatch=False;cab_activation=True
custom_control=False;actor_id="mock-cab-A";legacy_cab=False;mu_master=True;alternate_cab=None;iso_clock=False;neutral_crossings=0;station_cm=25000
automatic_control=False;emergency_gate=False;ice_control=False;throttle_value=0;ice_writes=[];reverser_output=1;br412=False;m7_control=False;release_gate=False
automatic_display={'namedValues':[{'valueSource':'Output','valueRange':{'min':lo,'max':hi},'displayName':label} for lo,hi,label in [(-2.5,-1.5,'紧急'),(-1.5,-.95,'最大制动'),(-.125,.5,'关闭'),(1,4,'{PowerNumber}')]]}
class Mock(BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def do_PATCH(self):
        global brake_value,neutral_crossings,throttle_value
        from urllib.parse import unquote, urlsplit, parse_qs
        route=urlsplit(unquote(self.path));calls.append((self.command,route.path,self.headers.get('DTGCommKey')))
        value=float(parse_qs(route.query)['Value'][0])
        assert (m7_control and '/MasterController.' in route.path) or (ice_control and ('/Throttle.' in route.path or '/TrainBrake.' in route.path)) or route.path.startswith('/set/CurrentDrivableActor/IrregularLever_ThrottleBrake.') or route.path.startswith('/set/CurrentDrivableActor/PowerHandle.') or route.path.startswith('/set/CurrentDrivableActor/TractionBrakeControl.')
        if route.path.endswith('.Interacting'):
            assert value in (0,1)
            interaction.append(value)
        else:
            assert route.path.endswith('.InputValue')
            if not mismatch:
                if ice_control:
                    if '/Throttle.' in route.path:throttle_value=value
                    else:brake_value=value
                    ice_writes.append((route.path,value,throttle_value))
                elif release_gate and vehicle.startswith('RVM_GOB_Class710_') and brake_value<=-.99 and value==0:brake_value=-.75
                elif release_gate and vehicle.startswith('RVM_GOB_Class710_') and abs(brake_value+.75)<.001 and value==0:brake_value=-.05
                elif emergency_gate and value==-1 and brake_value>-.75:brake_value=-.75
                elif vehicle.startswith('RVM_GOB_Class710_') and brake_value*value<0:brake_value=0;neutral_crossings+=1
                else:brake_value=value
        self.send_response(200);self.end_headers();self.wfile.write(b'{"Result":"Success"}')
    def do_GET(self):
        calls.append((self.command,self.path,self.headers.get('DTGCommKey')))
        if mode=='unauthorized': self.send_error(401); return
        if 'HUD_GetSpeed' in self.path:
            data={'Result':'Success','Values':{'Speed (ms)':speed_ms}}
            if mode=='ambiguous': data={'Result':'Success','Values':{'foo':5,'bar':10}}
        elif 'DriverAid.TrackData' in self.path:
            data={'Result':'Success','Values':{'stations':[{'stationName':'Live Station','distanceToStationCM':station_cm,'platformNumber':'2','platformSide':'Left'}],'trackHeights':[{'distanceToHeight':0,'height':10000},{'distanceToHeight':100000,'height':11000}]}}
        elif 'DriverAid.Data' in self.path:
            if mode=='aid_error': self.send_error(404); return
            data={'Result':'Success','Values':{'speedLimit':{'value':40},'speedLimitSeen':mode!='no_next','nextSpeedLimit':{'value':20},'distanceToNextSpeedLimit':150000,'nextSpeedLimits':[],'signalSeen':True,'signalAspectClass':'Stop','distanceToSignal':42000,'signalPropertyGuid':'test-signal','nextSignals':[{'value':'Clear','distanceToNextSignal':125000}]}}
            if mode=='no_next':data['Values'].update(nextSpeedLimit={'value':0},distanceToNextSpeedLimit=0)
            data['Values']['signalAspectClass']=signal_aspect
        elif 'Function.IsCabActive' in self.path:
            if legacy_cab:self.send_error(404);return
            data={'Result':'Success','Values':{('bReturnValue' if ice_control else 'ReturnValue'):cab_activation} if cab_activation is not None else {}}
        elif 'Function.GetIsCabActive' in self.path:
            data={'Result':'Success','Values':{'bIsCabActive':alternate_cab}}
        elif 'Property.KeySwitchOn' in self.path:
            data={'Result':'Success','Values':{'Value':cab_activation}}
        elif 'GetMasterKeyState' in self.path:
            data={'Result':'Success','Values':{'MasterKeyOn':cab_activation}}
        elif 'MTD_IsMUMaster' in self.path:
            data={'Result':'Success','Values':{'Result':mu_master}}
        elif 'HUD_GetMaxPermittedSpeed' in self.path:
            data={'Result':'Success','Values':{'IsActive':cab_active,'MaxSpeed (ms)':cab_ms,'WarningSpeed (ms)':cab_ms+2}}
        elif self.path.startswith('/list/CurrentDrivableActor/'):
            data={'Result':'Success','Endpoints':[{'Name':'InputValue','Writable':True},{'Name':'Interacting','Writable':True}]}
        elif self.path=='/list/CurrentDrivableActor':
            data={'Result':'Success','Nodes':[{'Name':'MasterController' if m7_control else 'PowerHandle'}]}
        elif control_model:
            from urllib.parse import unquote
            path=unquote(self.path);v={}
            if 'ObjectClass' in path:v={'ObjectClass':vehicle}
            elif 'ObjectName' in path:v={'ObjectName':actor_id}
            elif 'bCustomOutputValueMapping' in path:v={'Value':False}
            elif 'InputIdentifier' in path and automatic_control:v={'identifier':'Throttle'}
            elif 'DisplayInfo' in path and automatic_control:v=automatic_display
            elif 'GetMinimumOutputValue' in path:v={'ReturnValue':-2 if automatic_control else -4}
            elif 'GetMaximumOutputValue' in path:v={'ReturnValue':4 if automatic_control else 1 if vehicle.startswith('RVM_GOB_Class710_') else 7}
            elif 'GetNotchCount' in path:v={'ReturnValue':5}
            elif 'GetMinimumInputValue' in path:v={'ReturnValue':-1}
            elif 'GetMaximumInputValue' in path:v={'ReturnValue':1}
            elif 'GetCurrentOutputValue' in path:v={'ReturnValue':(-2 if brake_value<-.91 else brake_value/.9 if brake_value<0 else round(brake_value*4)) if automatic_control else (brake_value*4 if brake_value<0 else brake_value) if vehicle.startswith('RVM_GOB_Class710_') else (-2 if brake_value<=-.99 else -1 if brake_value<0 else round(brake_value*4)) if custom_control else (-4 if brake_value==0 else -round((.5-brake_value)*10) if brake_value<.5 else round((brake_value-.5)*14))}
            elif path.endswith('InputValue'):v={'InputValue':brake_value}
            elif 'TimeOfDay.Data' in path:v={'OriginLatitude':55.9518,'OriginLongitude':-3.1903,'LocalTimeISO8601':'frozen-clock' if mode=='freeze' else datetime.datetime.now(datetime.timezone.utc).isoformat() if iso_clock else str(time.monotonic())}
            if ice_control:
                is_throttle='/Throttle.' in path
                if 'InputIdentifier' in path:v={'identifier':'Throttle' if is_throttle else 'AutomaticBrake'}
                elif 'DisplayInfo' in path:v={'namedValues':[{'valueSource':'Output','valueRange':{'min':n-.5,'max':n+.5},'displayName':label} for n,label in [(0,'缓解'),(9,'全制动'),(10,'紧急情况')]]}
                elif 'GetMinimumInputValue' in path:v={'ReturnValue':0 if is_throttle or br412 else -.0833}
                elif 'GetMaximumInputValue' in path:v={'ReturnValue':1}
                elif 'GetMaximumOutputValue' in path:v={'ReturnValue':1 if is_throttle else 10}
                elif 'GetCurrentOutputValue' in path:v={'ReturnValue':throttle_value*2-1 if is_throttle else (0 if .05<brake_value<.15 else 10 if brake_value>=.9 else 9 if brake_value>=.7625 else -1 if brake_value<=.05 else max(1,round((brake_value-.1)*12))) if br412 else round(brake_value*10)}
                elif path.endswith('InputValue'):v={'InputValue':throttle_value if is_throttle else brake_value}
            if m7_control and '/MasterController.' in path:
                if 'InputIdentifier' in path:v={'identifier':'Throttle'}
                elif 'DisplayInfo' in path:v={'namedValues':[{'valueSource':'Output','valueRange':{'min':lo,'max':hi},'displayName':label} for lo,hi,label in [(-2.5,-1.5,'紧急制动'),(-1.5,-.995,'最大制动'),(-.025,.025,'空挡'),(.055,.995,'动力 {Power}'),(.995,1,'最大动力')]]}
                elif 'GetMinimumInputValue' in path:v={'ReturnValue':0}
                elif 'GetMaximumInputValue' in path:v={'ReturnValue':1}
                elif 'GetMinimumOutputValue' in path:v={'ReturnValue':-2}
                elif 'GetMaximumOutputValue' in path:v={'ReturnValue':1}
                elif 'GetCurrentOutputValue' in path:v={'ReturnValue':-2 if brake_value<.05 else -1 if brake_value<=.1 else (brake_value-.55)/.45}
                elif path.endswith('InputValue'):v={'InputValue':brake_value}
            if ice_control and br412 and '/TrainBrake.'  in path and 'DisplayInfo' in path:
                v={'namedValues':[{'valueSource':'Input','valueRange':{'min':lo,'max':hi},'displayName':label} for lo,hi,label in [(-.5,.05,'超驰控制乘客紧急制动'),(.05,.15,'行驶'),(.7625,.9,'全制动'),(.9,1.5,'紧急制动')]]}
            if ice_control and '/Reverser.' in path:
                if 'DisplayInfo' in path:v={'namedValues':[{'valueSource':'Output','valueRange':{'min':n-.5,'max':n+.5},'displayName':label} for n,label in ([(0,'空挡'),(1,'前牵位'),(2,'关闭')] if 'M7a-' in vehicle else [(0,'关闭'),(1,'前牵位')])]}
                elif 'GetCurrentOutputValue' in path:v={'ReturnValue':reverser_output}
                elif path.endswith('InputValue'):v={'InputValue':reverser_output}
            data={'Result':'Success','Values':v}
        else:data={'Result':'Success','Values':{}}
        self.send_response(200);self.send_header('Content-Type','application/json');self.end_headers();self.wfile.write(json.dumps(data).encode())
mock=ThreadingHTTPServer(('127.0.0.1',0),Mock)
threading.Thread(target=mock.serve_forever,daemon=True).start()
port=31272;base=f'http://127.0.0.1:{port}'
proc=subprocess.Popen([str(scratch/'DMI-Web.exe'),'--headless',f'--port={port}'],creationflags=subprocess.CREATE_NO_WINDOW)
count=0
def request(path,data=None,token=None,origin=base,extra=None):
    headers={}
    if data is not None:
        headers={'Content-Type':'application/json','Origin':origin,'X-DMI-Token':token or ''}
    if extra:headers.update(extra)
    req=urllib.request.Request(base+path,json.dumps(data).encode() if data is not None else None,headers=headers)
    try:
        with urllib.request.urlopen(req,timeout=5) as r:
            text=r.read().decode('utf-8-sig');return r.status,json.loads(text) if r.headers['Content-Type'].startswith('application/json') else text
    except urllib.error.HTTPError as e:return e.code,json.loads(e.read())
def ok(condition,label):
    global count
    assert condition,label
    count+=1;print('PASS',label)
def wait_frame(predicate,timeout=5):
    deadline=time.monotonic()+timeout
    while time.monotonic()<deadline:
        f=request('/api/frame')[1]
        if predicate(f):return f
        time.sleep(.1)
    raise AssertionError('frame condition timed out: '+json.dumps({k:f.get(k) for k in ['Vehicle','Ato','GameTime','GameAdvancing','Operations']}))
try:
    for attempt in range(30):
        try:bootstrap=request('/api/bootstrap')[1];break
        except OSError:time.sleep(.1)
    token=bootstrap['token']
    duplicate=subprocess.run([str(scratch/'DMI-Web.exe'),'--headless',f'--port={port}'],creationflags=subprocess.CREATE_NO_WINDOW,timeout=6)
    ok(duplicate.returncode==0,'duplicate launch reuses the running bridge')
    ok(request('/api/bootstrap')[1]['bridgeId']==bootstrap['bridgeId'] and proc.poll() is None,'duplicate launch preserves the original session')
    ok(not (scratch/'bridge-error.txt').exists(),'duplicate launch does not produce a false bridge failure')
    pending=request('/api/ato',{'action':'start'},token)
    ok(pending[0]==200 and pending[1]['waiting'] and pending[1]['enabled'],'ATO activation is accepted with disconnected game')
    ok(request('/api/ato',{'action':'stop'},token)[1]['enabled'] is False,'stop cancels disconnected pending activation')
    invalid=request('/api/ato',{'action':'configure','kp':-1},token)
    ok(invalid[0]==400 and request('/api/frame')[1]['Ato']['kp']==.65,'invalid PID cannot change the current configuration')
    tuned=request('/api/ato',{'action':'configure','allowCoast':True,'coastBand':3},token)
    ok(tuned[0]==200 and tuned[1]['allowCoast'] and tuned[1]['coastBand']==3,'coasting configuration is applied by the bridge')
    ok(request('/api/ato',{'action':'configure','coastBand':0},token)[0]==400,'invalid coasting band is rejected')
    request('/api/ato',{'action':'configure','coastBand':2},token)
    ok(request('/')[0]==200,'main page served')
    ok(request('/settings.html')[0]==200,'separate settings served')
    overlay=request('/overlay.html');ok(overlay[0]==200 and 'ato-panel' not in overlay[1] and 'main-ato.js' not in overlay[1],'overlay serves DMI buttons without ATO controls')
    ok(request('/api/frame')[1]['Speed'] is None,'no synthetic speed before connection')
    ok(request('/api/disconnect',{},token=None)[0]==403,'missing token rejected')
    ok(request('/api/disconnect',{},token,origin='https://example.com')[0]==403,'foreign origin rejected')
    ok(request('/api/frame',extra={'Host':'example.com'})[0]==403,'foreign host rejected')
    ok(request('/src/Bridge.cs')[0]==404,'source file not served')
    for audio in ['01','46']:
        with urllib.request.urlopen(base+'/sounds/lkj/'+audio+'.wav') as response:
            ok(response.status==200 and response.headers['Content-Type'].startswith('audio/wav') and response.read(12)[8:12]==b'WAVE','LKJ WAV '+audio+' served with correct media type')
    ok(request('/sounds/lkj/manifest.json')[0]==404,'LKJ metadata is outside static audio allowlist')
    with urllib.request.urlopen(base+'/sounds/custom/05.wav') as response:
        ok(response.status==200 and response.read(12)[8:12]==b'WAVE','local platform announcement is served as a WAV')


    config={'baseUrl':f'http://127.0.0.1:{mock.server_port}','key':secret}
    ok(request('/api/connect',config,token)[0]==200,'authenticated connection accepted')
    f=wait_frame(lambda f:f['Fresh'])
    ok(abs(f['Speed']-90)<.0001,'real response shape converts m/s to km/h')
    ok(f['Operations']['mode']=='PS','activated DMI boots in partial supervision on the bridge')
    ok(abs(f['Limit']-144)<.0001 and abs(f['Target']-72)<.0001,'limit units converted')
    ok(f['Distance']==1500,'centimetres converted to metres')
    ok(f['Station']=='Live Station','station read from game track endpoint')
    ok(f['StationDistance']==250 and f['Platform']=='2' and f['PlatformSide']=='左侧','station distance and provided platform side preserved')
    ok(f['Cab']['active'] is False and f['Cab']['permitted'] is None,'inactive cab zero never becomes a stop command')
    ok(f['Grades'][0]['grade']==10 and f['Grades'][0]['length']==1000,'grade derived from real track height segments')
    ok(secret not in (scratch/'bridge-settings.json').read_text(encoding='utf-8-sig'),'manual secret never persisted')
    mode='no_next';f=wait_frame(lambda f:f['Fresh'] and f['Target'] is None)
    ok(f['Limit']==144 and f['Distance'] is None,'unseen next limit preserves current limit and clears target')
    mode='aid_error';f=wait_frame(lambda f:f['Fresh'] and f['Limit'] is None)
    ok(f['Speed']==90,'missing line data preserves valid speed')
    mode='ambiguous';f=wait_frame(lambda f:not f['Fresh'])
    ok(f['Speed'] is None,'ambiguous speed clears stale values')
    mode='unauthorized'
    deadline=time.monotonic()+5
    while time.monotonic()<deadline and '鉴权失败' not in request('/api/status')[1]['error']:time.sleep(.1)
    ok('鉴权失败' in request('/api/status')[1]['error'],'authentication failure reported')
    ok(request('/api/query',{'path':'/set/anything'},token)[0]==400,'write route blocked')
    ok(all(c[0]=='GET' for c in calls),'only reads sent to game')
    ok(all(c[2]==secret for c in calls),'game requests authenticated')
    ok(request('/api/connect',{'baseUrl':'http://example.com','key':secret},token)[0]==400,'non-local game address rejected')
    ok(request('/api/disconnect',{},token)[0]==200,'disconnect succeeds')
    ok(request('/api/frame')[1]['Speed'] is None,'disconnect blanks telemetry')
    ok(json.loads((scratch/'bridge-settings.json').read_text(encoding='utf-8-sig'))['AutoConnect'] is False,'manual disconnect persists paused state')
    proc.terminate();proc.wait(timeout=5)
    (scratch/'mock-key.txt').write_text(secret)
    (scratch/'bridge-settings.json').write_text(json.dumps({'BaseUrl':f'http://127.0.0.1:{mock.server_port}','KeyFile':str(scratch/'mock-key.txt'),'AutoConnect':True}))
    mode='valid'
    proc=subprocess.Popen([str(scratch/'DMI-Web.exe'),'--headless',f'--port={port}'],creationflags=subprocess.CREATE_NO_WINDOW)
    for attempt in range(30):
        try:request('/api/bootstrap');break
        except OSError:time.sleep(.1)
    f=wait_frame(lambda f:f['Fresh'])
    ok(f['Speed']==90,'bridge startup connects automatically without clicking')
    mode='unauthorized';wait_frame(lambda f:not f['Fresh']);mode='valid'
    f=wait_frame(lambda f:f['Fresh'])
    ok(f['Speed']==90,'connection recovers automatically after API failure')
    bootstrap=request('/api/bootstrap')[1];token=bootstrap['token'];control=bootstrap['controlToken']
    speed_ms=0;control_model=True;f=wait_frame(lambda f:f['Brake']['supported'])
    ok(f['Signal']=='Stop' and f['SignalDistance']==420 and len(f['Signals'])==2,'game signals and distances are exposed')
    f=wait_frame(lambda f:bool(f['RouteId']))
    ok(f['RouteId']=='origin:55.9518,-3.1903','route identity comes from game origin')
    route={'routeId':f['RouteId'],'device':'CTCS-3','level':3,'mode':'FS'}
    ok(request('/api/route-profile',route,token)[0]==200,'route profile saved on host')
    ok(request('/api/frame')[1]['RouteProfile']['level']==3,'host route profile shared in live frames')
    ok((scratch/'route-profiles.json').exists(),'route profile persists separately from secrets')
    body={'vehicle':vehicle,'percent':100,'emergency':False}
    writes=lambda:len([c for c in calls if c[0]=='PATCH'])
    start=writes()
    ok(request('/api/brake',body,token=None)[0]==403,'unpaired brake input rejected')
    ok(request('/api/brake',{**body,'percent':101},token)[0]==400,'out-of-range brake input rejected')
    ok(request('/api/brake',{**body,'percent':'NaN'},token)[0]==400,'non-finite brake input rejected')
    ok(request('/api/brake',{**body,'vehicle':'stale-cab'},token)[0]==400,'stale cab command rejected')
    ok(writes()==start,'rejected commands never write to game')
    time.sleep(.2)
    status,result=request('/api/brake',body,control)
    ok(status==200 and result['matched'],'authorized brake command verifies game readback')
    ok(abs(brake_value-.2)<1e-8,'Class 170 full-service input maps to B3 detent')
    ok(interaction[-2:]==[1,0],'lever interaction uses numeric start and stop in actor namespace')
    ok(request('/api/disconnect',{},control)[0]==403,'control token cannot administer connection')
    ok(request('/api/brake',{**body,'emergency':True},token)[1]['matched'] and brake_value==0,'emergency brake maps to dedicated detent')
    ok(request('/api/brake',{**body,'percent':0},token)[1]['matched'] and brake_value==.5,'release maps to neutral, never to traction')
    before=writes();mode='ambiguous';wait_frame(lambda f:not f['Fresh'])
    ok(request('/api/brake',body,token)[0]==400 and writes()==before,'stale telemetry blocks game writes')
    mode='valid';wait_frame(lambda f:f['Fresh'])
    if bootstrap['lanUrls']:
        localbase=base;base=bootstrap['lanUrls'][0].rstrip('/')
        public=request('/api/bootstrap')[1]
        ok(public['token']=='' and public['controlToken']==control and public['canControl'] and public['settings'] is None,'LAN page automatically receives control permission but no administration or key path')
        ok(request('/api/raw')[0]==403,'raw game responses blocked on LAN')
        ok(request('/api/brake',body,token=None,origin=base)[0]==403,'LAN viewer cannot brake without pairing token')
        ok(request('/api/brake',body,public['controlToken'],origin=base)[0]==200,'ordinary LAN page can brake using its automatic session')
        ok(request('/api/query',{'path':'/info'},control,origin=base)[0]==403,'LAN controller cannot access arbitrary diagnostic paths')
        ok(request('/api/ato',{'action':'start'},origin=base)[0]==403,'unpaired LAN viewer cannot activate ATO')
        result=request('/api/ato',{'action':'configure','kp':.4,'ki':.05,'kd':.2},public['controlToken'],origin=base)
        ok(result[0]==200 and result[1]['kp']==.4 and result[1]['kd']==.2,'paired LAN operator can tune backend PID')
        result=request('/api/ato',{'action':'start'},public['controlToken'],origin=base)
        ok(result[0]==200 and result[1]['enabled'],'paired LAN operator can activate ATO')
        result=request('/api/ato',{'action':'stop'},public['controlToken'],origin=base)
        ok(result[0]==200 and not result[1]['enabled'],'paired LAN operator can stop ATO')
        request('/api/ato',{'action':'configure','kp':.65,'ki':.12,'kd':0},control,origin=base)
        base=localbase
    mode='freeze';f=wait_frame(lambda f:f.get('GameAdvancing') is False,timeout=12)
    before=writes()
    ok(request('/api/brake',body,token)[0]==400 and writes()==before,'frozen game clock blocks queued control inputs')
    mode='valid';wait_frame(lambda f:f.get('GameAdvancing') is True)
    ok(request('/api/brake',body,token)[0]==200,'resumed game clock re-enables explicit control input')
    # Exercise actual HTTP handlers with a controllable game API, not a browser-only fake.
    route.update(system='TVM430')
    ok(request('/api/route-profile',route,token)[0]==200,'TVM 430 route adapter selection persists')
    speed_ms=25;cab_active=True;cab_ms=20;signal_aspect='Clear'
    f=wait_frame(lambda f:f['Cab']['active'] and f['Signal']=='Clear')
    ok(f['Supervision']['permitted']==72 and f['Supervision']['system']=='TVM430','active game cab permission drives compatibility adapter')
    arm={'vehicle':vehicle,'device':'test-controller','protection':True,'throttle':0,'brake':0}
    ok(request('/api/mapping/start',arm,control)[0]==403,'brake pairing token cannot arm host joystick mapping')
    ok(request('/api/mapping/start',{**arm,'throttle':30},token)[0]==400,'non-neutral arming rejected')
    lease=request('/api/mapping/start',arm,token)[1]['lease']
    ok(request('/api/mapping/start',arm,token)[0]==400,'second mapping owner cannot seize active session')
    ok(lease not in json.dumps(request('/api/frame')[1]),'control lease never appears in viewer telemetry')
    ok(request('/api/brake',body,token)[0]==400,'manual service command cannot override active joystick protection')
    time.sleep(.2)
    drive={'lease':lease,'sequence':1,'device':'test-controller','throttle':80,'brake':0,'emergency':False}
    status,result=request('/api/mapping/input',drive,token)
    ok(status==200 and result['mapping']['emergencyLatched'] and result['mapping']['throttle']==0 and brake_value==0,'cab overspeed cuts traction and applies real emergency detent')
    ok(request('/api/mapping/ack',{'lease':lease},token)[0]==400,'emergency cannot be acknowledged while moving')
    ok(request('/api/mapping/input',drive,token)[0]==400,'replayed input sequence rejected')
    speed_ms=0;wait_frame(lambda f:f['Speed']==0)
    ok(request('/api/mapping/ack',{'lease':lease},token)[0]==200,'stationary acknowledgement clears emergency latch')
    time.sleep(.2);drive.update(sequence=2,throttle=100)
    result=request('/api/mapping/input',drive,token)[1]
    ok(result['result']['matched'] and brake_value==1,'traction mapping verified against real API input and output')
    speed_ms=75/3.6;wait_frame(lambda f:abs(f['Speed']-75)<.001)
    time.sleep(.2);drive.update(sequence=3)
    result=request('/api/mapping/input',drive,token)[1]
    ok(result['mapping']['serviceLatched'] and brake_value==.2,'three km/h overspeed applies full service brake')
    speed_ms=0;wait_frame(lambda f:f['Speed']==0)
    time.sleep(.2);drive.update(sequence=4,throttle=0)
    result=request('/api/mapping/input',drive,token)[1]
    ok(result['mapping']['serviceLatched'] and brake_value==.2,'service intervention remains latched after slowing')
    ok(request('/api/mapping/ack',{'lease':lease},token)[0]==200,'below-limit acknowledgement releases service latch')
    time.sleep(.2);drive.update(sequence=5)
    ok(request('/api/mapping/input',drive,token)[0]==200 and brake_value==.5,'acknowledged neutral input releases brake without traction')
    before=writes();time.sleep(2.8)
    ok(request('/api/mapping/input',{**drive,'sequence':6},token)[0]==400 and writes()==before,'expired input lease blocks all queued writes')
    lease=request('/api/mapping/start',arm,token)[1]['lease']
    mismatch=True;time.sleep(.2)
    drive.update(lease=lease,sequence=1,throttle=100)
    ok(request('/api/mapping/input',drive,token)[0]==400 and not request('/api/frame')[1]['Mapping']['armed'],'actual readback mismatch immediately disarms mapping')
    mismatch=False
    lease=request('/api/mapping/start',arm,token)[1]['lease']
    vehicle='RVM_BR_Class170_Another_C';wait_frame(lambda f:f['Vehicle']==vehicle)
    ok(not request('/api/frame')[1]['Mapping']['armed'],'vehicle switch stops the existing mapping session')
    arm['vehicle']=vehicle
    lease=request('/api/mapping/start',arm,token)[1]['lease']
    cab_activation=False
    f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    before=writes()
    ok(not f['DmiReady'] and f['Speed']==0,'inactive cab gates DMI while diagnostic telemetry remains real')
    ok(not f['Mapping']['armed'],'cab deactivation immediately disarms active mapping')
    ok(request('/api/brake',{**body,'vehicle':vehicle},token)[0]==400 and writes()==before,'inactive cab blocks direct brake writes')
    ok(request('/api/mapping/start',arm,token)[0]==400,'inactive cab cannot start joystick mapping')
    cab_activation=None
    f=wait_frame(lambda f:f['CabActivation']['active'] is None)
    ok(not f['DmiReady'] and f['Supervision']['permitted'] is None,'ambiguous cab activation cannot power DMI or protection')
    cab_activation=True
    f=wait_frame(lambda f:f['DmiReady'])
    ok(not f['Mapping']['armed'],'reactivation starts DMI but never resumes joystick automatically')
    operation={'vehicle':vehicle,'action':'vigilance'}
    ok(request('/api/operation',operation,control)[0]==200,'paired driver vigilance acknowledgement is functional')
    speed_ms=10;wait_frame(lambda f:f['Speed']==36)
    ok(request('/api/operation',{**operation,'action':'mode','mode':'OS'},token)[0]==400,'moving train cannot change operating mode')
    speed_ms=0;wait_frame(lambda f:f['Speed']==0)
    result=request('/api/operation',{**operation,'action':'mode','mode':'OS'},token)
    ok(result[0]==200 and result[1]['mode']=='OS','stopped OS selection synchronizes server operating mode')
    ok(request('/api/operation',{**operation,'action':'braketest'},token)[0]==200 and brake_value==.2,'stationary brake test checks actual service detent')
    ok(request('/api/operation',{**operation,'action':'release'},token)[0]==200 and brake_value==.5,'driver release operates actual neutral detent')
    vehicle='Custom_Class805_Test_C';custom_control=True;brake_value=0
    f=wait_frame(lambda f:f['Vehicle']==vehicle)
    ok(not f['Brake']['supported'],'unconfigured vehicle never borrows Class 170 mapping')
    result=request('/api/ato',{'action':'start'},control)
    ok(result[0]==200 and result[1]['waiting'],'unadapted vehicle still accepts pending ATO activation')
    request('/api/ato',{'action':'stop'},control)
    calibration={'vehicle':vehicle,'control':'CurrentDrivableActor/PowerHandle'}
    before=writes()
    for point,value in [('neutral',0),('power',1),('service',-.8),('emergency',-1)]:
        brake_value=value
        ok(request('/api/vehicle-map',{**calibration,'point':point},token)[0]==200,'record custom vehicle '+point)
    ok(writes()==before,'vehicle calibration only reads positions from game')
    ok(request('/api/vehicle-map',{**calibration,'point':'save'},token)[0]==200,'four-point calibrated vehicle profile persists')
    f=wait_frame(lambda f:f['Brake']['supported'])
    ok(f['Brake']['emergency'] and f['Brake']['control']=='CurrentDrivableActor/PowerHandle','calibrated emergency is semantic and not hard-coded to output -4')
    time.sleep(.2)
    result=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':False},token)
    ok(result[0]==200 and result[1]['matched'] and brake_value==-.8,'custom full-service input uses its recorded native range')
    result=request('/api/operation',{'vehicle':vehicle,'action':'release'},token)
    ok(result[0]==200 and brake_value==0,'custom neutral does not reuse Class 170 half-axis input')
    speed_ms=30;wait_frame(lambda f:f['Speed']==108)
    arm.update(vehicle=vehicle)
    lease=request('/api/mapping/start',arm,token)[1]['lease']
    time.sleep(.2)
    protected={'lease':lease,'sequence':1,'device':'test-controller','throttle':0,'brake':0,'emergency':False}
    ok(request('/api/mapping/input',protected,token)[1]['mapping']['emergencyLatched'],'new cab overspeed produces scoped emergency latch')
    request('/api/mapping/stop',{'lease':lease},token)
    before=writes()
    ok(request('/api/brake',{'vehicle':vehicle,'percent':0},token)[0]==400 and writes()==before,'stopping mapping does not bypass penalty release conditions')
    speed_ms=0;vehicle='Custom_OtherCab_Test_C'
    f=wait_frame(lambda f:f['Vehicle']==vehicle)
    ok(not f['Mapping']['emergencyLatched'],'switching vehicle clears old-cab penalty rather than braking the new vehicle')
    vehicle='Custom_Class805_Test_C';speed_ms=0;wait_frame(lambda f:f['Vehicle']==vehicle and f['Speed']==0)
    arm.update(vehicle=vehicle);lease=request('/api/mapping/start',arm,token)[1]['lease']
    actor_id='mock-cab-B'
    f=wait_frame(lambda f:f['VehicleId']==actor_id)
    ok(not f['Mapping']['armed'],'switching to another cab of the same class stops mapping')
    vehicle='RVM_LDN_Class484_C';legacy_cab=True
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['DmiReady'])
    ok('MUMaster' in f['CabActivation']['source'],'Class 484 validates actual key and MU-master states when generic cab API is absent')
    mu_master=False;f=wait_frame(lambda f:not f['DmiReady'])
    ok(f['CabActivation']['active'] is False,'key-on alone cannot activate an unmastered Class 484 cab')
    mu_master=True;cab_activation=False;f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    ok(not f['DmiReady'],'Class 484 key-off keeps DMI powered down')
    vehicle='RVM_RIV_XC_Class220_DMSL_C';alternate_cab=True
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['DmiReady'])
    ok(f['CabActivation']['source'].endswith('GetIsCabActive'),'Class 220 alternate activation endpoint powers DMI')
    alternate_cab=False;f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    ok(not f['DmiReady'],'Class 220 deactivation powers DMI down')
    alternate_cab=1;f=wait_frame(lambda f:f['CabActivation']['active'] is None)
    ok(not f['DmiReady'],'numeric alternate activation is not accepted as a boolean')
    alternate_cab=True;legacy_cab=False;cab_activation=False
    f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    ok(not f['DmiReady'],'explicit primary inactive state is never overridden by an alternate endpoint')
    ok(request('/ato.html')[0]==200,'independent ATO page is served')
    ato_request={'action':'start','vehicle':'RVM_BR_Class170_Test_C','station':'Live Station','offset':0,'maxSpeed':60,'deceleration':.5}
    result=request('/api/ato',ato_request,control)
    ok(result[0]==200 and result[1]['waiting'],'paired control token can activate ATO even with inactive cab')
    vehicle='RVM_BR_Class170_Test_C';custom_control=False;cab_activation=True;legacy_cab=False;iso_clock=True;speed_ms=0;cab_active=False;signal_aspect='Clear';brake_value=.5
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['Brake']['supported'] and 'T' in f['GameTime'] and f['DmiReady'])
    f=wait_frame(lambda f:f['Ato']['active'])
    ok(f['Ato']['station']=='Live Station','pending activation automatically starts when game and vehicle become ready')
    request('/api/ato',{'action':'stop'},control)
    request('/api/operation',{'action':'mode','mode':'FS','vehicle':vehicle},token)
    result=request('/api/ato',ato_request,token)
    ok(result[0]==200 and result[1]['active'],'host explicitly starts ATO for current next station')
    f=wait_frame(lambda f:f['Ato']['throttle']>0)
    ok(f['Ato']['station']=='Live Station' and brake_value>.5,'ATO issues actual verified traction input')
    cab_active=True;cab_ms=0
    f=wait_frame(lambda f:f['Ato']['targetSpeed']==0 and f['Ato']['brake']==100)
    ok(f['Ato']['active'] and brake_value==.2,'zero cab authority holds ATO with real service brake')
    cab_active=False
    f=wait_frame(lambda f:f['Ato']['throttle']>0)
    ok(f['Ato']['active'],'authority restoration resumes active ATO toward locked target')
    arm.update(vehicle=vehicle)
    ok(request('/api/mapping/start',arm,token)[0]==400,'joystick cannot compete with active ATO')
    ok(request('/api/operation',{'vehicle':vehicle,'action':'release'},token)[0]==400,'release cannot overwrite active ATO commands')
    time.sleep(.2)
    result=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':False},token)
    f=wait_frame(lambda f:not f['Ato']['active'])
    ok(result[0]==200 and brake_value==.2,'manual service brake cancels ATO before applying driver command')
    result=request('/api/ato',ato_request,token)
    ok(result[0]==200,'ATO can be explicitly restarted after driver takeover')
    result=request('/api/ato',{'action':'stop'},token)
    ok(result[0]==200 and not result[1]['active'] and brake_value==.2,'ATO stop applies verified full service brake')
    request('/api/ato',ato_request,token)
    mode='freeze'
    f=wait_frame(lambda f:not f['Ato']['active'],timeout=8)
    ok(brake_value==.2,'paused game exits ATO and writes service brake before resume')
    mode='valid';wait_frame(lambda f:f['GameAdvancing'] is True)
    ok(not request('/api/frame')[1]['Ato']['active'],'game resume never automatically rearms ATO')
    request('/api/ato',ato_request,token)
    wait_frame(lambda f:f['Ato']['active'])
    actor_id='another-ato-cab'
    f=wait_frame(lambda f:f['VehicleId']==actor_id and not f['Ato']['active'])
    ok(not f['Ato']['active'],'ATO cannot continue in another same-class cab')
    result=request('/api/ato',{**ato_request,'offset':float('nan')},token)
    ok(result[0]==400,'invalid ATO numeric parameters rejected at HTTP boundary')
    vehicle='RVM_GOB_Class710_DMS_A_C';custom_control=False;speed_ms=0;brake_value=.5
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['Brake']['supported'])
    ok(f['Brake']['control']=='CurrentDrivableActor/TractionBrakeControl','Class 710 is ready without manual calibration')
    result=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':False},token)
    ok(result[0]==200 and result[1]['matched'] and brake_value==-.75,'Class 710 automatic service map verifies game readback')
    ok(neutral_crossings==1,'neutral gate is crossed by releasing interaction then retrying once')
    request('/api/operation',{'vehicle':vehicle,'action':'mode','mode':'FS'},token)
    result=request('/api/ato',{**ato_request,'vehicle':vehicle},token)
    ok(result[0]==200 and result[1]['active'],'Class 710 ATO starts directly without a vehicle-map file')
    result=request('/api/ato',{'action':'stop'},token)
    ok(result[0]==200 and brake_value==-.75,'Class 710 ATO exit holds its own service detent')
    ok('main-ato-start' in request('/')[1] and request('/main-ato.js')[0]==200,'main DMI page exposes ATO controls and script')
    request('/api/operation',{'vehicle':vehicle,'action':'mode','mode':'OS'},token)
    arm.update(vehicle=vehicle)
    request('/api/mapping/start',arm,token)
    result=request('/api/ato',{'action':'start'},token)
    ok(result[0]==200 and result[1]['active'],'single-click ATO reads current vehicle and station without FS precondition')
    ok(not request('/api/frame')[1]['Mapping']['armed'],'ATO automatically takes over active joystick mapping')
    repeat=request('/api/ato',{'action':'start'},token)
    ok(repeat[0]==200 and repeat[1]['active'],'clicking start during ATO returns state instead of rejecting')
    request('/api/ato',{'action':'stop'},token)
    station_cm=500
    wait_frame(lambda f:f['StationDistance']==5)
    result=request('/api/ato',{'action':'start'},token)
    ok(result[0]==200,'station closer than ten metres no longer disables ATO start')
    request('/api/ato',{'action':'stop'},token)
    station_cm=None
    wait_frame(lambda f:f['StationDistance'] is None)
    result=request('/api/ato',{'action':'start'},control)
    ok(result[0]==200 and result[1]['active'] and not result[1]['hasDestination'],'ATO starts without a destination through paired control API')
    f=wait_frame(lambda f:f['Ato']['targetSpeed']>0)
    ok(f['Ato']['remaining'] is None and f['Ato']['routing']=='limits' and len(f['Ato']['curve'])>2,'cruise publishes target curve without fabricated destination distance')
    station_cm=50000
    f=wait_frame(lambda f:f['Ato']['hasDestination'])
    ok(f['Ato']['station']=='Live Station','restored destination is automatically locked by running ATO')
    request('/api/ato',{'action':'stop'},control)
    automatic_control=True;vehicle='Unknown_Combined_Controller_C';brake_value=1;speed_ms=0;before=writes()
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['Brake']['supported'])
    ok(f['Brake']['control']=='CurrentDrivableActor/PowerHandle' and writes()==before,'unknown vehicle is discovered from game metadata without calibration writes')
    r=request('/api/brake',{'vehicle':vehicle,'percent':100},token)
    ok(r[0]==200 and r[1]['matched'] and abs(r[1]['readbackOutput']+1)<.035,'automatic controller locates full service by real output feedback')
    r=request('/api/brake',{'vehicle':vehicle,'percent':50},token)
    ok(r[0]==200 and r[1]['matched'] and abs(r[1]['readbackOutput']+.5)<.035,'automatic controller supports proportional service braking')
    r=request('/api/brake',{'vehicle':vehicle,'percent':0},token)
    ok(r[0]==200 and r[1]['matched'] and abs(r[1]['readbackOutput'])<.035,'automatic controller finds neutral without an input calibration')
    r=request('/api/ato',{'action':'start'},token)
    f=wait_frame(lambda f:f['Brake']['output']==4)
    ok(r[0]==200 and f['Ato']['active'] and f['Ato']['throttle']==100,'ATO reaches verified P4 through automatic output control')
    request('/api/ato',{'action':'stop'},token)
    r=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':True},token)
    ok(r[0]==200 and r[1]['matched'] and r[1]['readbackOutput']==-2,'automatic controller distinguishes emergency from service braking')
    automatic_control=False;custom_control=False;vehicle='RVM_GOB_Class710_ATP_C';speed_ms=0;brake_value=0;emergency_gate=True
    wait_frame(lambda f:f['Vehicle']==vehicle and f['Speed']==0)
    r=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':True},token)
    ok(r[0]==200 and r[1]['matched'] and brake_value==-1,'Class 710 emergency command crosses the service detent gate')
    request('/api/brake',{'vehicle':vehicle,'percent':0},token)
    cab_active=True;cab_ms=20;speed_ms=30
    f=wait_frame(lambda f:f['Mapping']['emergencyLatched'] and f['Brake']['emergency'])
    ok(not f['Ato']['active'] and not f['Mapping']['armed'],'ATP applies emergency without ATO or joystick ownership')
    before=writes();brake_value=1
    f=wait_frame(lambda f:f['Brake']['emergency'] and brake_value==-1 and writes()>before)
    ok(brake_value==-1,'latched ATP restores emergency after another input releases the handle')
    ok(request('/api/operation',{'vehicle':vehicle,'action':'release'},token)[0]==400,'independent ATP cannot be released while moving')
    speed_ms=0;wait_frame(lambda f:f['Speed']==0)
    ok(request('/api/operation',{'vehicle':vehicle,'action':'release'},token)[0]==200,'independent ATP releases after stopping and explicit acknowledgement')
    ok(request('/api/ato',{'action':'start','platformEndMargin':501},token)[0]==400,'platform end margin outside range is rejected')
    r=request('/api/ato',{'action':'start','platformEndMargin':30},token)
    ok(r[0]==200 and r[1]['platformEndMargin']==30,'ATO accepts custom platform end margin over HTTP')
    request('/api/ato',{'action':'stop'},token)
    request('/api/ato',{'action':'start'},token)
    wait_frame(lambda f:f['Ato']['active'])
    speed_ms=30
    f=wait_frame(lambda f:f['Speed']==108 and f['Ato']['brake']>0)
    ok(f['Ato']['active'] and f['AtpSuspended'] and not f['Mapping']['emergencyLatched'],'ATO overspeed uses its controller while ATP intervention is suspended')
    request('/api/ato',{'action':'stop'},token)
    f=wait_frame(lambda f:f['Mapping']['emergencyLatched'] and f['Brake']['emergency'])
    ok(not f['AtpSuspended'],'ATP automatically resumes after ATO exit')
    ice_control=True;automatic_control=False;vehicle='RVM_KAH_DB_ICE3M_EndCar-5_C';speed_ms=0;brake_value=0;throttle_value=0
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['Brake']['supported'])
    ok(f['Brake']['control']=='CurrentDrivableActor/TrainBrake','ICE separate brake and throttle are discovered from verified metadata')
    throttle_value=.8;ice_writes.clear()
    r=request('/api/brake',{'vehicle':vehicle,'percent':100},token)
    ok(r[0]==200 and r[1]['matched'] and r[1]['readbackOutput']==9 and throttle_value==0,'ICE full service brake cuts traction and verifies brake output nine')
    ok(all(t==0 for path,value,t in ice_writes if '/TrainBrake.' in path),'ICE never applies brake while traction remains commanded')
    r=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':True},token)
    ok(r[0]==200 and r[1]['readbackOutput']==10,'ICE emergency uses output ten and maximum input')
    r=request('/api/brake',{'vehicle':vehicle,'percent':0},token)
    ok(r[0]==200 and r[1]['readbackOutput']==0 and throttle_value==0,'ICE release verifies zero brake without adding power')
    request('/api/ato',{'action':'start'},token)
    f=wait_frame(lambda f:throttle_value>0 and f['Ato']['active'])
    ok(round(brake_value*10)==0,'ICE ATO applies traction only after verified brake release')
    request('/api/ato',{'action':'stop'},token)
    ok(throttle_value==0 and round(brake_value*10)==9,'ICE ATO exit cuts power and holds service brake')
    cab_activation=None;alternate_cab=None;reverser_output=1
    f=wait_frame(lambda f:f['DmiReady'] and '德铁换向器' in f['CabActivation']['source'])
    ok(f['CabActivation']['active'],'DB unsupported activation endpoint falls back to labelled forward reverser')
    reverser_output=0
    f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    ok(not f['DmiReady'],'returning DB reverser to off powers down DMI')
    cab_activation=False;reverser_output=1
    time.sleep(.8);f=request('/api/frame')[1]
    ok(not f['DmiReady'] and '德铁换向器' not in f['CabActivation']['source'],'explicit inactive cab overrides forward reverser fallback')
    br412=True;vehicle='RVM_NWB_DB_BR412_EW2-H_C';cab_activation=None;reverser_output=1;brake_value=.1
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['Brake']['supported'] and f['DmiReady'])
    ok(f['Brake']['output']==0,'BR412 input-labelled brake is recognized without manual calibration')
    r=request('/api/brake',{'vehicle':vehicle,'percent':100},token)
    ok(r[0]==200 and r[1]['matched'] and r[1]['readbackOutput']==9,'BR412 full service is found by actual output feedback')
    r=request('/api/brake',{'vehicle':vehicle,'percent':0},token)
    ok(r[0]==200 and r[1]['readbackOutput']==0 and brake_value>.05,'BR412 release selects running position instead of emergency override')
    r=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':True},token)
    ok(r[0]==200 and r[1]['readbackOutput']==10,'BR412 emergency remains distinct from full service')
    vehicle='RVM_NYH_MNR_M7a-A_C';cab_activation=None;alternate_cab=None;reverser_output=0
    f=wait_frame(lambda f:f['Vehicle']==vehicle and f['DmiReady'])
    ok('M7A 换向器' in f['CabActivation']['source'],'M7A neutral reverser activates DMI through labelled fallback')
    reverser_output=2
    f=wait_frame(lambda f:f['CabActivation']['active'] is False)
    ok(not f['DmiReady'],'M7A off reverser shuts down DMI')
    m7_control=True;ice_control=False;automatic_control=False;brake_value=.1;cab_activation=True
    f=wait_frame(lambda f:f['Brake']['supported'],timeout=15)
    ok(f['Brake']['control']=='CurrentDrivableActor/MasterController','M7A maximum power Chinese label identifies its combined controller')
    r=request('/api/brake',{'vehicle':vehicle,'percent':100},token)
    ok(r[0]==200 and r[1]['matched'] and abs(r[1]['readbackOutput']+1)<.035,'M7A service brake verifies actual output minus one')
    time.sleep(.2)
    r=request('/api/brake',{'vehicle':vehicle,'percent':0},token)
    ok(r[0]==200 and abs(r[1]['readbackOutput'])<.035,'M7A neutral is located by output rather than assumed input midpoint')
    request('/api/ato',{'action':'start'},token)
    f=wait_frame(lambda f:f['Ato']['active'] and f['Brake']['output']>.9)
    ok(f['Ato']['active'],'M7A ATO can command verified traction')
    request('/api/ato',{'action':'stop'},token)
    r=request('/api/brake',{'vehicle':vehicle,'percent':100,'emergency':True},token)
    ok(r[0]==200 and r[1]['readbackOutput']==-2,'M7A emergency remains distinct from service braking')
    m7_control=False;ice_control=False;automatic_control=False;vehicle='RVM_GOB_Class710_Release_C';speed_ms=0;cab_activation=True;brake_value=-1;release_gate=True
    wait_frame(lambda f:f['Vehicle']==vehicle and f['DmiReady'])
    r=request('/api/operation',{'vehicle':vehicle,'action':'release'},token)
    ok(r[0]==200 and brake_value==0,'Class 710 release crosses emergency, service and minimum-brake gates before confirming neutral')
    print(f'{count} bridge tests passed.')
finally:
    proc.terminate();proc.wait(timeout=5);mock.shutdown();mock.server_close()

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using TSW7DMI;

namespace CTCSWeb {
public class Bridge : IDisposable {
    readonly object gate=new object();readonly string root,token=Guid.NewGuid().ToString("N");readonly List<TcpListener> listeners=new List<TcpListener>();readonly List<IPAddress> addresses=new List<IPAddress>();
    readonly string bridgeId=Guid.NewGuid().ToString("N");
    readonly string controlToken=Guid.NewGuid().ToString("N");readonly object controlGate=new object();DateTime lastControl=DateTime.MinValue;
    Dictionary<string,DriveMap> vehicleMaps=new Dictionary<string,DriveMap>();CalibrationDraft calibration;
    Dictionary<string,object> routeProfiles=new Dictionary<string,object>();
    readonly MappingSession mapping=new MappingSession();readonly Operations operations=new Operations();AxisMapping nativeMapping;bool nativeAck,nativeVigilance;
    readonly AtoSession ato=new AtoSession();
    volatile bool running=true;Settings settings=new Settings();ApiClient client;Frame frame=new Frame();int generation;bool enabled,fileKey=true;string error="",rawSpeed="",rawAid="";public readonly int Port;
    public Bridge(string directory,int port,bool lanEnabled=true){root=Path.GetFullPath(Path.Combine(directory,"dist"));Port=port;settings=LoadSettings();addresses.Add(IPAddress.Loopback);
        if(lanEnabled)foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()){
            if(nic.OperationalStatus!=OperationalStatus.Up||!(nic.NetworkInterfaceType==NetworkInterfaceType.Ethernet||nic.NetworkInterfaceType==NetworkInterfaceType.Wireless80211))continue;
            var info=nic.GetIPProperties();if(!info.GatewayAddresses.Any(g=>g.Address.AddressFamily==AddressFamily.InterNetwork&&!g.Address.Equals(IPAddress.Any)))continue;
            foreach(var item in info.UnicastAddresses)if(IsPrivateIPv4(item.Address)&&!addresses.Contains(item.Address))addresses.Add(item.Address);
        }
        foreach(var address in addresses)listeners.Add(new TcpListener(address,port));
        try{vehicleMaps=new JavaScriptSerializer().Deserialize<Dictionary<string,DriveMap>>(File.ReadAllText(Path.Combine(directory,"vehicle-mappings.json"),Encoding.UTF8))??vehicleMaps;}catch{}
        try{routeProfiles=Parse(File.ReadAllText(Path.Combine(directory,"route-profiles.json"),Encoding.UTF8)) as Dictionary<string,object>??routeProfiles;}catch{}
    }
    static bool IsPrivateIPv4(IPAddress address){if(address.AddressFamily!=AddressFamily.InterNetwork)return false;var b=address.GetAddressBytes();return b[0]==10||(b[0]==172&&b[1]>=16&&b[1]<=31)||(b[0]==192&&b[1]==168);}
    string[] LanUrls(){return addresses.Where(a=>!IPAddress.IsLoopback(a)).Select(a=>"http://"+a+":"+Port+"/").ToArray();}
    bool AllowedHost(string host){return host=="localhost:"+Port||addresses.Any(a=>host==a.ToString()+":"+Port);}
    Settings LoadSettings(){try{return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(root),"bridge-settings.json"),Encoding.UTF8))??new Settings();}catch{return new Settings();}}
    public void Start(){NativeJoystick.Start();Task.Run((Action)NativeControlLoop);foreach(var listener in listeners){listener.Start();var activeListener=listener;Task.Run(()=>Accept(activeListener));}lock(gate){enabled=settings.AutoConnect;if(enabled)error="正在自动连接 TSW 7";}Task.Run((Action)Poll);}
    public void Dispose(){lock(controlGate){AtoFailStop("桥接正在退出，ATO 已停止");running=false;foreach(var listener in listeners)listener.Stop();lock(gate){enabled=false;client=null;generation++;}}}
    void Accept(TcpListener listener){while(running){try{var socket=listener.AcceptTcpClient();Task.Run(()=>Serve(socket));}catch{if(running)Thread.Sleep(50);}}}
    string ReadKey(Settings cfg){string file=cfg.KeyFile.Length>0?cfg.KeyFile:FindKey();if(!File.Exists(file))throw new ArgumentException("等待 TSW 7 接口密钥：请以 -HTTPAPI 启动游戏并进入驾驶室。");return File.ReadAllText(file,Encoding.UTF8).Trim().Trim('\uFEFF');}
    void SaveSettings(){File.WriteAllText(Path.Combine(Path.GetDirectoryName(root),"bridge-settings.json"),Json(settings),new UTF8Encoding(true));}
    void Poll(){while(running){ApiClient current;Settings cfg;int gen;bool active;lock(gate){current=client;cfg=settings;gen=generation;active=enabled;}
        bool failed=false;
        if(active){try{
            if(current==null){current=new ApiClient(cfg,ReadKey(cfg)){MapResolver=FindMap};lock(gate){if(gen==generation&&enabled){client=current;fileKey=true;}}}
            Frame f=current.Read(cfg);lock(gate){if(gen==generation&&enabled){frame=f;error=f.Error;rawSpeed=f.RawSpeed;rawAid=f.RawAid;}}
        }catch(Exception e){failed=true;lock(gate){if(gen==generation&&enabled){frame=new Frame{Live=true};error=e.Message;rawSpeed=current==null?"":current.LastSpeedResponse;rawAid=current==null?"":current.LastAidResponse;if(fileKey)client=null;}}}}
        lock(controlGate){lock(gate){CheckMapping();TickOperations();}}
        Thread.Sleep(failed?1500:350);
    }}
    string Json(object x){return new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Serialize(x);}
    object Parse(string x){return new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(x);}
    string Input(Dictionary<string,object> d,string key,string fallback=""){object x;return d.TryGetValue(key,out x)&&x!=null?Convert.ToString(x):fallback;}
    string FindKey(){string docs=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),user=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);return new[]{Path.Combine(docs,"My Games","TrainSimWorld7","Saved","Config","CommAPIKey.txt"),Path.Combine(user,"Documents","My Games","TrainSimWorld7","Saved","Config","CommAPIKey.txt")}.FirstOrDefault(File.Exists)??"";}
    object PublicSettings(){return new {baseUrl=settings.BaseUrl,keyFile=settings.KeyFile,speedEndpoint=settings.SpeedEndpoint,speedField=settings.SpeedField,speedUnit=settings.SpeedUnit,aidEndpoint=settings.AidEndpoint,aidSpeedUnit=settings.AidSpeedUnit,aidDistanceUnit=settings.AidDistanceUnit,trackEndpoint=settings.TrackEndpoint,autoConnect=settings.AutoConnect};}
    DriveMap FindMap(string vehicle){lock(gate){DriveMap result;return vehicleMaps.TryGetValue(vehicle,out result)&&result.Valid()?result:DriveMap.Builtin(vehicle);}}
    void CheckMapping(){if(!mapping.armed)return;
        if(!enabled||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3)mapping.Stop("TSW 数据失效，已退出接管；游戏保持最后档位");
        else if(frame.Vehicle!=mapping.vehicle||frame.VehicleId!=mapping.cab||frame.RouteId!=mapping.route)mapping.Stop("车辆或线路已切换，请重新校准并启用");
        else if(frame.CabActivated!=true)mapping.Stop("司机室激活已撤销，DMI 关闭并退出接管");
        else if(frame.GameAdvancing==false)mapping.Stop("游戏已暂停，恢复后需重新启用摇杆");
        else if((DateTime.UtcNow-mapping.inputAt).TotalSeconds>2.5)mapping.Stop("摇杆输入超时，已退出接管；游戏保持最后档位");
    }
    bool dmiCabPowered;string dmiCabIdentity="";
    void TickOperations(){
        if(frame.Fresh&&(mapping.vehicle!=frame.Vehicle||mapping.cab!=frame.VehicleId||mapping.route!=frame.RouteId)){mapping.emergencyLatched=mapping.serviceLatched=false;}
        bool powered=frame.Fresh&&frame.CabActivated==true;
        string identity=frame.Vehicle+"|"+frame.VehicleId+"|"+frame.RouteId;
        bool starting=powered&&(!dmiCabPowered||dmiCabIdentity!=identity);
        string configured=routeProfiles.ContainsKey(frame.RouteId)?Convert.ToString(JsonData.At(routeProfiles[frame.RouteId],"mode")??"FS"):"FS";
        operations.Tick(frame,configured);
        if(starting){operations.requestedMode="PS";operations.Tick(frame,configured);operations.Event("DMI 启动完成，进入部分监控模式");}
        dmiCabPowered=powered;dmiCabIdentity=identity;
    }

    string SelectedSystem(){return routeProfiles.ContainsKey(frame.RouteId)?Convert.ToString(JsonData.At(routeProfiles[frame.RouteId],"system")??"AUTO"):"AUTO";}
    Frame ControlFrame(string vehicle){lock(gate){if(!enabled||client==null||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3)throw new InvalidOperationException("TSW 数据失效，未发送操纵指令。");if(frame.CabActivated!=true)throw new InvalidOperationException("司机室未激活或无法验证，DMI 尚未启动。");if(frame.GameAdvancing==false)throw new InvalidOperationException("游戏时间未推进，请先恢复游戏。");if(frame.Vehicle!=vehicle)throw new InvalidOperationException("车辆已切换，请等待新数据。");return frame;}}
    double Percent(Dictionary<string,object> body,string key){double value;if(!Double.TryParse(Input(body,key),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||Double.IsNaN(value)||Double.IsInfinity(value)||value<0||value>100)throw new ArgumentException(key+" 必须在 0–100 范围内。");return value;}
    object MappingCommand(string path,Dictionary<string,object> body){lock(controlGate){
        lock(gate)CheckMapping();
        if(path=="/api/mapping/start"){
            if(ato.active)throw new InvalidOperationException("请先退出 ATO 再启用摇杆。");
            if(mapping.armed)throw new InvalidOperationException("已有摇杆页面正在接管，请先在该页面停止。");
            Frame current=ControlFrame(Input(body,"vehicle"));if(!current.BrakeSupported)throw new InvalidOperationException("当前车型没有可用的真实操纵映射。");
            if(Percent(body,"throttle")>1||Percent(body,"brake")>1)throw new InvalidOperationException("请先把摇杆回到空挡，避免启用时跳档。");
            string device=Input(body,"device");if(device.Length<1||device.Length>256)throw new ArgumentException("请先选择已连接的摇杆。");
            nativeMapping=null;if(device.StartsWith("sdl:")||device.StartsWith("winmm:")||device.StartsWith("xinput:")){
                object config;if(!body.TryGetValue("config",out config))throw new ArgumentException("缺少摇杆轴配置");var parsed=new JavaScriptSerializer().Deserialize<AxisMapping>(Json(config));var input=parsed.Read(NativeJoystick.Read().FirstOrDefault(d=>d.id==device));if(input.throttle>1||input.brake>1||input.emergency)throw new InvalidOperationException("请将实际摇杆回空挡并松开紧急制动按钮。");nativeMapping=parsed;nativeAck=input.ack;nativeVigilance=input.vigilance;
            }
            mapping.protection=Object.Equals(JsonData.At(body,"protection"),true);
            if(mapping.protection&&!Protection.Evaluate(current,settings,SelectedSystem()).permitted.HasValue)throw new InvalidOperationException("没有有效线路或车载限速，暂不能启用列控干预。");
            mapping.lease=Guid.NewGuid().ToString("N");mapping.vehicle=current.Vehicle;mapping.cab=current.VehicleId;mapping.route=current.RouteId;mapping.device=device;mapping.sequence=0;mapping.inputAt=DateTime.UtcNow;mapping.armed=true;mapping.throttle=mapping.brake=0;mapping.Event("摇杆已接管"+(mapping.protection?" · 列控干预已启用":" · 仅手动映射"));return new{lease=mapping.lease,message=mapping.message};
        }
        if(!mapping.armed||Input(body,"lease")!=mapping.lease)throw new InvalidOperationException("接管会话已失效，请回空挡重新启用。");
        if(path=="/api/mapping/stop"){mapping.Stop("已停止摇杆映射；游戏保持最后档位");return mapping.Status();}
        Frame active=ControlFrame(mapping.vehicle);ApiClient apiClient;string system;lock(gate){apiClient=client;system=SelectedSystem();}
        var supervision=Protection.Evaluate(active,settings,system);mapping.supervision=supervision;
        if(path=="/api/mapping/ack"){
            if(mapping.throttle>1||!supervision.permitted.HasValue||active.Speed>supervision.permitted||(mapping.emergencyLatched&&active.Speed>.5))throw new InvalidOperationException("缓解条件未满足：牵引归零、速度低于允许速度；紧急制动需停车。");
            mapping.emergencyLatched=mapping.serviceLatched=false;operations.penalty=false;operations.Vigilance();mapping.Event("司机已确认，允许缓解");return mapping.Status();
        }
        if(path!="/api/mapping/input")throw new FileNotFoundException();
        long sequence;if(!Int64.TryParse(Input(body,"sequence"),out sequence)||sequence<=mapping.sequence)throw new ArgumentException("拒绝过期或重复的摇杆输入。");
        double throttle=Percent(body,"throttle"),brake=Percent(body,"brake");
        if(Input(body,"device")!=mapping.device){mapping.Stop("摇杆已改变，退出接管");throw new InvalidOperationException(mapping.message);}
        if((DateTime.UtcNow-lastControl).TotalMilliseconds<150)throw new ArgumentException("指令过于频繁。");lastControl=DateTime.UtcNow;
        if(mapping.protection&&!supervision.permitted.HasValue){mapping.Stop("列控数据丢失，已退出接管；游戏保持最后档位");throw new InvalidOperationException(mapping.message);}
        mapping.sequence=sequence;mapping.inputAt=DateTime.UtcNow;
        bool emergency=Object.Equals(JsonData.At(body,"emergency"),true);mapping.emergencyLatched|=emergency||(mapping.protection&&(supervision.emergency||operations.penalty));
        mapping.serviceLatched|=mapping.protection&&supervision.brake>0;
        if(mapping.emergencyLatched||mapping.serviceLatched){brake=100;throttle=0;}else if(brake>0)throttle=0;
        mapping.throttle=throttle;mapping.brake=brake;
        try{var result=apiClient.SetDrive(throttle,brake,mapping.emergencyLatched,mapping.vehicle,mapping.cab);
            if(!Object.Equals(JsonData.At(result,"matched"),true)){mapping.Stop("实际手柄回读不匹配，已退出接管；检查其他输入或车型校准");throw new InvalidOperationException(mapping.message);}
            mapping.Event(mapping.emergencyLatched?"紧急制动已锁存 · 停车后确认":mapping.serviceLatched?"常用制动干预 · 降速后确认缓解":supervision.status=="warning"?"接近或超过允许速度，请减速":"摇杆接管中 · 游戏档位已匹配");
            return new{result=result,mapping=mapping.Status()};
        }catch(Exception e){mapping.Stop(e.Message);throw;}
    }}
    DateTime lastPenaltyWrite=DateTime.MinValue;
    void PenaltyTick(){
        Frame f;ApiClient api;ProtectionState supervision;
        lock(gate){TickOperations();f=frame;api=client;if(!enabled||api==null||!f.Fresh||(DateTime.UtcNow-f.Timestamp).TotalSeconds>=3||f.CabActivated!=true||f.GameAdvancing!=true)return;supervision=Protection.Evaluate(f,settings,SelectedSystem());}
        bool supervise=!ato.active&&operations.mode!="IS"&&operations.mode!="SL";
        if(operations.penalty||(supervise&&(supervision.emergency||supervision.brake>0))){mapping.vehicle=f.Vehicle;mapping.cab=f.VehicleId;mapping.route=f.RouteId;mapping.emergencyLatched|=operations.penalty||supervision.emergency;mapping.serviceLatched|=supervision.brake>0;}
        if(!mapping.emergencyLatched&&!mapping.serviceLatched)return;
        mapping.supervision=supervision;mapping.throttle=0;mapping.brake=100;
        if(ato.active||ato.waiting){ato.Stop("ATP 惩罚制动锁存，停车确认后才能缓解");ato.brake=100;ato.emergency=mapping.emergencyLatched;}
        if((DateTime.UtcNow-lastPenaltyWrite).TotalMilliseconds<500)return;
        lastPenaltyWrite=DateTime.UtcNow;
        if(mapping.emergencyLatched?f.BrakeEmergency:f.BrakeService){mapping.Event("ATP 惩罚制动已锁存 · 实际制动档位已确认");return;}
        try{var result=api.SetDrive(0,100,mapping.emergencyLatched,f.Vehicle,f.VehicleId);if(!Object.Equals(JsonData.At(result,"matched"),true))throw new InvalidOperationException("实际手柄回读不匹配");mapping.Event("ATP 惩罚制动已锁存 · 实际制动档位已确认");}
        catch(Exception e){mapping.Event("ATP 制动未确认，将重试："+e.Message);}
    }
    void NativeControlLoop(){while(running){lock(controlGate){PenaltyTick();AtoTick();if(mapping.armed&&nativeMapping!=null){try{
        var input=nativeMapping.Read(NativeJoystick.Read().FirstOrDefault(d=>d.id==mapping.device));
        if(input.vigilance&&!nativeVigilance)OperationCommand(new Dictionary<string,object>{{"action","vigilance"},{"vehicle",mapping.vehicle}});
        if(input.ack&&!nativeAck){try{MappingCommand("/api/mapping/ack",new Dictionary<string,object>{{"lease",mapping.lease}});}catch(Exception e){mapping.Event(e.Message);}}
        nativeAck=input.ack;nativeVigilance=input.vigilance;
        MappingCommand("/api/mapping/input",new Dictionary<string,object>{{"lease",mapping.lease},{"sequence",mapping.sequence+1},{"device",mapping.device},{"throttle",input.throttle},{"brake",input.brake},{"emergency",input.emergency}});
    }catch(Exception e){mapping.Stop(e.Message);nativeMapping=null;}}}Thread.Sleep(180);}}
    void AtoFailStop(string reason){
        bool wasActive=ato.active;ato.Stop(reason);if(!wasActive)return;
        Frame f;ApiClient api;lock(gate){f=frame;api=client;}
        if(api==null||!f.Fresh||(DateTime.UtcNow-f.Timestamp).TotalSeconds>=3||f.CabActivated!=true||f.Vehicle!=ato.vehicle||f.VehicleId!=ato.cab||f.RouteId!=ato.route){ato.message+="；无法验证当前车辆，未写入制动，请在游戏中接管";return;}
        try{var result=api.SetDrive(0,100,ato.emergency||mapping.emergencyLatched||operations.penalty,ato.vehicle,ato.cab);if(!Object.Equals(JsonData.At(result,"matched"),true))throw new InvalidOperationException("回读不匹配");ato.brake=100;ato.message+=(ato.emergency||mapping.emergencyLatched||operations.penalty)?"；已施加紧急制动":"；已施加常用制动";}catch(Exception e){ato.message+="；制动未确认："+e.Message;}
    }
    void TryStartWaitingAto(){if(!ato.waiting)return;try{
        Frame current;lock(gate){if(!enabled||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3)throw new InvalidOperationException("等待 TSW 有效数据");current=frame;}
        if(current.CabActivated!=true)throw new InvalidOperationException("等待司机室激活验证");
        if(current.GameAdvancing!=true)throw new InvalidOperationException("等待游戏时间恢复推进");
        if(!current.BrakeSupported)throw new InvalidOperationException("正在自动识别游戏操纵控件；当前接口尚未提供完整档位信息");
        if(!Protection.Evaluate(current,settings,SelectedSystem()).permitted.HasValue)throw new InvalidOperationException("等待线路或车载限速");
        ato.Start(current,ato.offset,ato.maxSpeed,ato.deceleration);
        ato.targetSpeed=ato.TargetFor(0,Protection.Evaluate(current,settings,SelectedSystem()).permitted.Value);ato.UpdateCurve(current,settings);
        if(mapping.armed){mapping.Stop("ATO 已接管，摇杆映射已停止");nativeMapping=null;}
    }catch(Exception e){ato.message="ATO 已激活 · "+e.Message;}}
    void AtoTick(){TryStartWaitingAto();if(!ato.active)return;try{
        Frame f;Settings cfg;ApiClient api;string system;lock(gate){f=frame;cfg=settings;api=client;system=SelectedSystem();TickOperations();}
        if(!enabled||(DateTime.UtcNow-f.Timestamp).TotalSeconds>=3)throw new InvalidOperationException("TSW 数据过期，ATO 已退出");
        var protection=Protection.Evaluate(f,cfg,system);
        bool changed=ato.Step(f,protection,operations.penalty||mapping.emergencyLatched||mapping.serviceLatched);ato.UpdateCurve(f,cfg);if(!changed)return;
        var result=api.SetDrive(ato.throttle,ato.brake,ato.emergency,ato.vehicle,ato.cab);
        if(!Object.Equals(JsonData.At(result,"matched"),true))throw new InvalidOperationException("ATO 手柄回读不匹配：请求 "+JsonData.At(result,"input")+"，实际输入 "+JsonData.At(result,"readbackInput")+"，实际输出 "+JsonData.At(result,"readbackOutput"));
        if(ato.phase=="protection"||ato.emergency){mapping.vehicle=f.Vehicle;mapping.cab=f.VehicleId;mapping.route=f.RouteId;mapping.emergencyLatched|=ato.emergency;mapping.serviceLatched|=!ato.emergency;ato.Stop(ato.message);}
        else if(ato.arrived)ato.Stop(ato.message);
    }catch(Exception e){AtoFailStop(e.Message);}}
    object AtoCommand(Dictionary<string,object> body){lock(controlGate){
        string action=Input(body,"action");if(action=="stop"){AtoFailStop("司机已退出 ATO");return ato.Status();}
        if(action=="skip"){Frame current=ControlFrame(ato.vehicle);ato.SkipStation(current);ato.targetSpeed=ato.TargetFor(0,Protection.Evaluate(current,settings,SelectedSystem()).permitted??0);ato.UpdateCurve(current,settings);return ato.Status();}
        if(action!="start"&&action!="configure")throw new ArgumentException("ATO 操作无效");
        double? offset=body.ContainsKey("offset")?JsonData.Number(JsonData.At(body,"offset")):0,ceiling=body.ContainsKey("maxSpeed")?JsonData.Number(JsonData.At(body,"maxSpeed")):120,decel=body.ContainsKey("deceleration")?JsonData.Number(JsonData.At(body,"deceleration")):settings.Deceleration;if(!offset.HasValue||!ceiling.HasValue||!decel.HasValue)throw new ArgumentException("停车偏移、最高速度或减速度无效");
        AtoSession.ValidateOptions(offset.Value,ceiling.Value,decel.Value);
        double? platformMargin=body.ContainsKey("platformEndMargin")?JsonData.Number(JsonData.At(body,"platformEndMargin")):ato.platformEndMargin;
        if(!platformMargin.HasValue||Double.IsNaN(platformMargin.Value)||Double.IsInfinity(platformMargin.Value)||platformMargin<0||platformMargin>500)throw new ArgumentException("距站台末端须为 0–500 m。");
        double? p=body.ContainsKey("kp")?JsonData.Number(JsonData.At(body,"kp")):ato.kp,i=body.ContainsKey("ki")?JsonData.Number(JsonData.At(body,"ki")):ato.ki,d=body.ContainsKey("kd")?JsonData.Number(JsonData.At(body,"kd")):ato.kd,limit=body.ContainsKey("integralLimit")?JsonData.Number(JsonData.At(body,"integralLimit")):ato.integralLimit,filter=body.ContainsKey("derivativeFilter")?JsonData.Number(JsonData.At(body,"derivativeFilter")):ato.derivativeFilter;
        if(!p.HasValue||!i.HasValue||!d.HasValue||!limit.HasValue||!filter.HasValue)throw new ArgumentException("PID 参数必须为有效数值");
        AtoSession.ValidatePid(p.Value,i.Value,d.Value,limit.Value,filter.Value);
        double? band=body.ContainsKey("coastBand")?JsonData.Number(JsonData.At(body,"coastBand")):ato.coastBand;
        if(!band.HasValue||band<.5||band>10||body.ContainsKey("allowCoast")&&!(body["allowCoast"] is bool))throw new ArgumentException("惰行开关须为布尔值，回差范围为 0.5–10 km/h。");
        if(action=="start"&&(ato.active||ato.waiting))return ato.Status();
        ato.ConfigurePid(p.Value,i.Value,d.Value,limit.Value,filter.Value);
        ato.allowCoast=body.ContainsKey("allowCoast")?(bool)body["allowCoast"]:ato.allowCoast;ato.coastBand=band.Value;
        if(action=="configure")return ato.Status();
        ato.platformEndMargin=platformMargin.Value;ato.Request(offset.Value,ceiling.Value,decel.Value);TryStartWaitingAto();
        return ato.Status();
    }}
    object Calibrate(Dictionary<string,object> body){lock(controlGate){
        if(ato.active)throw new InvalidOperationException("请先退出 ATO 再校准车型。");
        if(mapping.armed)throw new InvalidOperationException("请先停止摇杆接管再校准车型。");Frame current=ControlFrame(Input(body,"vehicle"));if(current.Speed>.5)throw new InvalidOperationException("车型档位校准需要停车，并在游戏中保持列车制动。");ApiClient api;lock(gate)api=client;
        string control=Input(body,"control"),point=Input(body,"point");
        if(point=="save"){
            if(calibration==null||calibration.vehicle!=current.Vehicle||calibration.control!=control)throw new ArgumentException("此车型校准记录不存在。");var map=api.FinishCalibration(calibration);
            lock(gate){vehicleMaps[current.Vehicle]=map;File.WriteAllText(Path.Combine(Path.GetDirectoryName(root),"vehicle-mappings.json"),Json(vehicleMaps),new UTF8Encoding(true));}return new{message="车型组合手柄校准已保存；回空挡后启用摇杆，并核对游戏回读。"};
        }
        if(!new[]{"neutral","power","service","emergency"}.Contains(point))throw new ArgumentException("校准档位无效。");
        var capture=api.CaptureLever(control);if(calibration==null||calibration.vehicle!=current.Vehicle||calibration.control!=control)calibration=new CalibrationDraft{vehicle=current.Vehicle,control=control};calibration.points[point]=capture;
        return new{message="已记录游戏实际档位",points=calibration.points};
    }}
    object OperationCommand(Dictionary<string,object> body){lock(controlGate){
        Frame current;ApiClient apiClient;string system;
        lock(gate){if(!enabled||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3||frame.CabActivated!=true)throw new InvalidOperationException("司机室激活验证未通过，操作不可用。");current=frame;apiClient=client;system=SelectedSystem();TickOperations();}
        if(Input(body,"vehicle")!=current.Vehicle)throw new InvalidOperationException("车辆已改变，请等待新数据。");
        string action=Input(body,"action");
        if(ato.active&&action!="vigilance")throw new InvalidOperationException("ATO 正在接管，请先退出 ATO 再操作。");
        if(action=="vigilance"){operations.Vigilance();return operations.Status();}
        if(action=="mode"||action=="start"){
            string next=action=="start"?"FS":Input(body,"mode");
            if(!new[]{"FS","PS","OS","CO","SH","SB","IS","CS","SL","TR","PT","LKJ"}.Contains(next)||current.Speed>.5)throw new InvalidOperationException("模式转换或启动需在停车状态下进行。");
            if(mapping.armed)throw new InvalidOperationException("请先停止摇杆接管再转换模式。");
            if(operations.penalty||mapping.emergencyLatched||mapping.serviceLatched)throw new InvalidOperationException("请先完成停车确认和制动缓解。");
            operations.requestedMode=next;operations.ResetTimer();operations.Event("DMI 训练模式已切换："+next);lock(gate)TickOperations();return operations.Status();
        }
        if(action=="release"){
            var supervision=Protection.Evaluate(current,settings,system);
            if(((operations.penalty||mapping.emergencyLatched)&&current.Speed>.5)||(mapping.serviceLatched&&(!supervision.permitted.HasValue||current.Speed>supervision.permitted))||mapping.throttle>1&&mapping.armed)throw new InvalidOperationException("尚不允许缓解：牵引需归零，紧急／警惕制动需停车，常用干预需降至允许速度。");
            if(mapping.armed){mapping.emergencyLatched=mapping.serviceLatched=false;operations.penalty=false;operations.Vigilance();mapping.Event("允许缓解；下一帧按司机手柄输入执行");}
            else if(current.BrakeSupported){ControlFrame(current.Vehicle);var result=apiClient.SetBrake(0,false,current.Vehicle,current.VehicleId);if(!Object.Equals(JsonData.At(result,"matched"),true))throw new InvalidOperationException("缓解指令回读不匹配");mapping.emergencyLatched=mapping.serviceLatched=false;operations.penalty=false;operations.Vigilance();}
            else{if(current.Speed>.5)throw new InvalidOperationException("当前车型无写入适配，请停车后在游戏中缓解");operations.penalty=false;operations.Vigilance();}
            operations.Event(current.BrakeSupported?"缓解已确认，查看实际手柄回读":"训练制动已复位；当前车型请在游戏中缓解实际制动");return operations.Status();
        }
        if(action=="braketest"){
            ControlFrame(current.Vehicle);if(current.Speed>.5||mapping.armed||!current.BrakeSupported)throw new InvalidOperationException("制动测试需要停车、停止摇杆接管，并使用已支持的车型。");
            var result=apiClient.SetBrake(100,false,current.Vehicle,current.VehicleId);bool matched=Object.Equals(JsonData.At(result,"matched"),true);operations.brakeTest=matched?"常用制动档位回读匹配":"制动档位回读不匹配";operations.Event(operations.brakeTest+"；测试后需手动缓解，未测量制动缸压力");return operations.Status();
        }
        throw new ArgumentException("操作不存在");
    }}
    object Snapshot(){object control,operationState,atoState;lock(controlGate){lock(gate){CheckMapping();TickOperations();}control=mapping.Status();operationState=operations.Status();atoState=ato.Status();}lock(gate){bool fresh=enabled&&frame.Fresh&&(DateTime.UtcNow-frame.Timestamp).TotalSeconds<3;var profile=new List<object>();if(fresh&&frame.Limit.HasValue){profile.Add(new {distance=0.0,speed=frame.Limit.Value});if(frame.Target.HasValue&&frame.Distance.HasValue)profile.Add(new {distance=frame.Distance.Value,speed=frame.Target.Value});
        try{var a=JsonData.At(JsonData.Parse(frame.RawAid),"Values.nextSpeedLimits") as object[];if(a!=null)foreach(var item in a){var v=JsonData.Number(JsonData.At(item,"value"));var d=JsonData.Number(JsonData.At(item,"distanceToNextSpeedLimit"));if(v.HasValue&&d.HasValue&&v>=0&&d>=0)profile.Add(new{distance=JsonData.ToMetres(d.Value,settings.AidDistanceUnit),speed=JsonData.ToKmh(v.Value,settings.AidSpeedUnit)});}}catch{}}
        return new {BridgeId=bridgeId,Fresh=fresh,Live=true,DmiReady=fresh&&frame.CabActivated==true,CabActivation=new{active=fresh?frame.CabActivated:null,source=frame.CabActivationSource,reason=fresh?frame.CabActivationReason:"等待 TSW 连接后验证司机室"},Speed=fresh?frame.Speed:null,Limit=fresh?frame.Limit:null,Target=fresh?frame.Target:null,Distance=fresh?frame.Distance:null,Timestamp=frame.Timestamp.ToString("o"),Profile=profile,Grades=fresh?frame.Grades:new object[0],Station=fresh?frame.Station:"",StationDistance=fresh?frame.StationDistance:null,PlatformLength=fresh?frame.PlatformLength:null,Platform=fresh?frame.Platform:"",PlatformSide=fresh?frame.PlatformSide:"",PlatformInfo=fresh?frame.PlatformInfo:null,LineConditions=fresh?frame.LineConditions:new object[0],Stations=fresh?frame.Stations:new object[0],Cab=new{active=fresh&&frame.CabActive,permitted=fresh?frame.CabPermitted:null,warning=fresh?frame.CabWarning:null,systems=fresh?frame.AvailableSystems:new string[0]},Supervision=fresh?Protection.Evaluate(frame,settings,SelectedSystem()):new ProtectionState(),Mapping=control,Operations=operationState,Ato=atoState,AtpSuspended=ato.active,Signals=fresh?frame.Signals:new object[0],Signal=fresh?frame.Signal:"",SignalId=fresh?frame.SignalId:"",SignalDistance=fresh?frame.SignalDistance:null,RouteId=fresh?frame.RouteId:"",RouteProfile=fresh&&routeProfiles.ContainsKey(frame.RouteId)?routeProfiles[frame.RouteId]:null,Vehicle=fresh?frame.Vehicle:"",VehicleId=fresh?frame.VehicleId:"",Service=fresh?frame.Service:"",GameTime=fresh?frame.GameTime:"",GameAdvancing=fresh?frame.GameAdvancing:null,Acceleration=fresh?frame.Acceleration:null,Brake=new{supported=fresh&&frame.BrakeSupported,ready=fresh&&frame.BrakeSupported&&frame.CabActivated==true,input=fresh?frame.BrakeInput:null,output=fresh?frame.BrakeOutput:null,emergency=fresh&&frame.BrakeEmergency,service=fresh&&frame.BrakeService,control=fresh?frame.BrakeControl:""},Version="tsw-ato-6"};}}
    object Api(string path,string method,Dictionary<string,object> body,bool local){
        if(method=="GET") {
            if(path=="/api/bootstrap")lock(gate)return new{product="TSW7-DMI-Web",bridgeId=bridgeId,token=local?token:"",controlToken=controlToken,canControl=true,settings=local?PublicSettings():null,port=Port,canManageConnection=local,lanUrls=LanUrls()};
            if(path=="/api/frame")return Snapshot();
            if(path=="/api/levers"){ApiClient current;lock(gate)current=client;if(current==null)throw new ArgumentException("请先连接游戏。");return new{controls=current.LeverControls()};}
            if(path=="/api/joysticks")return new{devices=NativeJoystick.Read(),message=NativeJoystick.Error};
            if(path=="/api/status")lock(gate)return new{enabled=enabled,fresh=enabled&&frame.Fresh&&(DateTime.UtcNow-frame.Timestamp).TotalSeconds<3,error=local?error:(enabled&&!frame.Fresh?"等待主机连接 TSW 7":"")};
            if(path=="/api/raw")lock(gate)return new{speed=rawSpeed,aid=rawAid};
        }
        if(method=="POST") {
            if(path=="/api/ato")return AtoCommand(body);
            if(path=="/api/vehicle-map")return Calibrate(body);
            if(path=="/api/operation")return OperationCommand(body);
            if(path.StartsWith("/api/mapping/",StringComparison.Ordinal))return MappingCommand(path,body);
            if(path=="/api/route-profile"){
                string id=Input(body,"routeId"),device=Input(body,"device"),mode=Input(body,"mode"),system=Input(body,"system","AUTO");int level;
                if(!Protection.Systems.Contains(system))throw new ArgumentException("列控兼容系统无效。");
                if(!Int32.TryParse(Input(body,"level"),out level)||!new[]{0,2,3}.Contains(level)||!new[]{"CTCS-2","CTCS-3"}.Contains(device)||device=="CTCS-2"&&level==3||!new[]{"FS","PS","OS","CO","SH","SB","IS","CS","SL","TR","PT","LKJ"}.Contains(mode))throw new ArgumentException("线路档案内容无效。");
                lock(controlGate){if(ato.active)throw new ArgumentException("请先退出 ATO 再更改线路系统。");if(mapping.armed)throw new ArgumentException("请先停止摇杆映射再更改线路系统。");lock(gate){if(!enabled||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3||id!=frame.RouteId||id.Length==0)throw new ArgumentException("线路已改变或数据失效。");operations.requestedMode="";routeProfiles[id]=new{device=device,level=level,mode=mode,system=system};File.WriteAllText(Path.Combine(Path.GetDirectoryName(root),"route-profiles.json"),Json(routeProfiles),new UTF8Encoding(true));}}
                return new{message="线路档案已保存至主机，所有局域网主屏自动加载。"};
            }
            if(path=="/api/brake"){
                double percent;if(!Double.TryParse(Input(body,"percent"),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out percent)||Double.IsNaN(percent)||Double.IsInfinity(percent)||percent<0||percent>100)throw new ArgumentException("制动输入范围为 0–100%。");
                object emergencyValue;bool emergency=body.TryGetValue("emergency",out emergencyValue)&&Object.Equals(emergencyValue,true);
                lock(controlGate){lock(gate)TickOperations();if(!emergency&&(mapping.emergencyLatched||mapping.serviceLatched||operations.penalty))throw new InvalidOperationException("训练制动已锁存，请满足条件后使用允许缓解操作。");if(mapping.armed){if(!emergency)throw new InvalidOperationException("摇杆正在接管，请先停止映射；紧急制动始终可用。");mapping.emergencyLatched=true;mapping.Stop("手动紧急制动，已停止摇杆映射");}if(!emergency&&(DateTime.UtcNow-lastControl).TotalMilliseconds<150)throw new ArgumentException("指令过于频繁。");lastControl=DateTime.UtcNow;ApiClient current;string vehicle;lock(gate){if(!enabled||client==null||!frame.Fresh||(DateTime.UtcNow-frame.Timestamp).TotalSeconds>=3)throw new InvalidOperationException("游戏数据失效，未发送制动指令。");if(frame.CabActivated!=true)throw new InvalidOperationException("司机室未激活或无法验证，DMI 尚未启动。");if(frame.GameAdvancing==false)throw new InvalidOperationException("游戏时间未推进，请先恢复游戏，再输入制动。");current=client;vehicle=frame.Vehicle;if(vehicle!=Input(body,"vehicle"))throw new InvalidOperationException("车辆已切换，请等待新数据。");}ato.Stop("司机手动制动接管，ATO 已退出");string cab;lock(gate)cab=frame.VehicleId;return current.SetBrake(percent,emergency,vehicle,cab);}
            }
            if(path=="/api/connect") {
                var cfg=new Settings{BaseUrl=Input(body,"baseUrl","http://127.0.0.1:31270").Trim(),KeyFile=Input(body,"keyFile").Trim(),SpeedEndpoint=Input(body,"speedEndpoint","CurrentDrivableActor.Function.HUD_GetSpeed").Trim(),SpeedField=Input(body,"speedField").Trim(),SpeedUnit=Input(body,"speedUnit","m/s"),AidEndpoint=Input(body,"aidEndpoint","DriverAid.Data").Trim(),AidSpeedUnit=Input(body,"aidSpeedUnit","m/s"),AidDistanceUnit=Input(body,"aidDistanceUnit","cm"),TrackEndpoint=Input(body,"trackEndpoint","DriverAid.TrackData").Trim(),AutoConnect=true};
                if(cfg.SpeedEndpoint.Length==0||!new[]{"m/s","km/h","mph","cm/s"}.Contains(cfg.SpeedUnit)||!new[]{"m/s","km/h","mph","cm/s"}.Contains(cfg.AidSpeedUnit)||!new[]{"cm","m","km"}.Contains(cfg.AidDistanceUnit))throw new ArgumentException("端点或单位无效。");
                string secret=Input(body,"key").Trim();bool supplied=secret.Length>0;if(!supplied)secret=ReadKey(cfg);
                var next=new ApiClient(cfg,secret){MapResolver=FindMap};lock(controlGate){AtoFailStop("连接配置改变，ATO 已退出");mapping.Stop("连接配置已改变，摇杆接管已停止");lock(gate){settings=cfg;client=next;fileKey=!supplied;enabled=true;generation++;frame=new Frame{Live=true};error="等待 TSW 7 接口响应";rawSpeed=rawAid="";}}
                SaveSettings();return new{message="开始连接 TSW 7；收到有效速度后主屏显示遥测。手动密钥不会保存。"};
            }
            if(path=="/api/disconnect"){lock(controlGate){AtoFailStop("连接断开，ATO 已退出");mapping.Stop("游戏连接已断开，摇杆接管已停止");lock(gate){enabled=false;settings.AutoConnect=false;client=null;generation++;frame=new Frame{Live=true};error="";SaveSettings();}}return new {message="已断开，自动连接已暂停"};}
            if(path=="/api/query") {string route=Input(body,"path");if(!(route=="/info"||route=="/list"||route.StartsWith("/list/",StringComparison.Ordinal)||route.StartsWith("/get/",StringComparison.Ordinal))||route.Contains(".."))throw new ArgumentException("仅支持 /info、/list 和 /get 只读查询。");ApiClient current;lock(gate)current=client;if(current==null)throw new ArgumentException("请先配置密钥并连接。");string text=current.Get(route);return new{data=JsonData.Parse(text)};}
        }
        throw new FileNotFoundException("接口不存在");
    }
    void Respond(NetworkStream stream,int status,string type,byte[] data){string reason=status==200?"OK":status==400?"Bad Request":status==403?"Forbidden":status==404?"Not Found":"Error";string headers="HTTP/1.1 "+status+" "+reason+"\r\nContent-Type: "+type+"\r\nContent-Length: "+data.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data: blob:; frame-ancestors 'self'\r\n\r\n";byte[] h=Encoding.ASCII.GetBytes(headers);stream.Write(h,0,h.Length);stream.Write(data,0,data.Length);}
    void TextResponse(NetworkStream stream,int status,string text){Respond(stream,status,"application/json; charset=utf-8",Encoding.UTF8.GetBytes(text));}
    void Serve(TcpClient socket){using(socket){var peer=((IPEndPoint)socket.Client.RemoteEndPoint).Address;bool local=IPAddress.IsLoopback(peer);if(!local&&!IsPrivateIPv4(peer))return;socket.ReceiveTimeout=4000;socket.SendTimeout=4000;using(var stream=socket.GetStream()){
        try{var bytes=new List<byte>();int next;while((next=stream.ReadByte())>=0){bytes.Add((byte)next);int n=bytes.Count;if(n>16384)throw new ArgumentException("请求头过大");if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break;}
            if(bytes.Count<4)return;string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None),first=lines[0].Split(' ');if(first.Length!=3)throw new ArgumentException("请求无效");string method=first[0],target=first[1];var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(string line in lines.Skip(1)){int colon=line.IndexOf(':');if(colon>0)headers[line.Substring(0,colon)]=line.Substring(colon+1).Trim();}
            string host;if(!headers.TryGetValue("Host",out host)||!AllowedHost(host)){TextResponse(stream,403,Json(new{error="Host 无效"}));return;}
            string path=target.Split('?')[0];if(!path.StartsWith("/",StringComparison.Ordinal)||path.Contains("..")||path.Contains("%")||path.Contains("\\"))throw new ArgumentException("路径无效");
            var body=new Dictionary<string,object>();
            if(method=="POST") {
                string len;int count;
                if(!headers.TryGetValue("Content-Length",out len)||!Int32.TryParse(len,out count)||count<0||count>1024*1024)throw new ArgumentException("请求体长度无效");
                // Consume the bounded body before rejecting, so TCP close does not discard the JSON error.
                byte[] data=new byte[count];int read=0;
                while(read<count){int n=stream.Read(data,read,count-read);if(n==0)throw new ArgumentException("请求不完整");read+=n;}
                string auth,origin,type;
                bool brake=path=="/api/brake"||path=="/api/operation"||path=="/api/ato";
                if(!headers.TryGetValue("X-DMI-Token",out auth)||!(local&&auth==token||brake&&auth==controlToken)||!headers.TryGetValue("Origin",out origin)||origin!="http://"+host||!headers.TryGetValue("Content-Type",out type)||!type.StartsWith("application/json",StringComparison.OrdinalIgnoreCase)){
                    TextResponse(stream,403,Json(new{error="请求来源或会话无效"}));return;
                }
                body=Parse(Encoding.UTF8.GetString(data)) as Dictionary<string,object>;if(body==null)throw new ArgumentException("JSON 须为对象");
            }
            if(path.StartsWith("/api/",StringComparison.Ordinal)){if(!local&&!(method=="GET"&&new[]{"/api/bootstrap","/api/frame","/api/status"}.Contains(path))&&!(method=="POST"&&(path=="/api/brake"||path=="/api/operation"||path=="/api/ato"))){TextResponse(stream,403,Json(new{error="游戏连接与接口诊断仅允许在主机上管理。"}));return;}TextResponse(stream,200,Json(Api(path,method,body,local)));return;}
            if(method!="GET")throw new FileNotFoundException();if(path=="/")path="/index.html";string[] allowed={"index.html","overlay.html","settings.html","dmi.css","settings.css","core.js","dmi.js","settings.js","sound.js","control.html","control.js","joystick.html","joystick.js","mapping.js","ato.html","ato.js","ato-controls.js","main-ato.js","sounds/S1_toofast.wav","sounds/S2_warning.wav","sounds/S_click.wav","sounds/S_info.wav","sounds/notch.wav"};string name=path.Substring(1);if(!allowed.Contains(name)&&!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Asounds/custom/0[1-7]\.wav\z")&&!System.Text.RegularExpressions.Regex.IsMatch(name,@"\Asounds/lkj/(0[1-9]|[1-3][0-9]|4[0-6])\.wav\z"))throw new FileNotFoundException();string full=Path.Combine(root,name);byte[] content=File.ReadAllBytes(full);string ext=Path.GetExtension(full),mime=ext==".html"?"text/html":ext==".js"?"text/javascript":ext==".wav"?"audio/wav":"text/css";Respond(stream,200,mime+"; charset=utf-8",content);
        }catch(FileNotFoundException){try{TextResponse(stream,404,Json(new{error="页面不存在"}));}catch{}}
        catch(Exception e){try{TextResponse(stream,400,Json(new{error=e.Message}));}catch{}}
    }}}
}
class Tray : ApplicationContext {
    readonly NotifyIcon icon;readonly Bridge bridge;
    public Tray(Bridge b){bridge=b;var menu=new ContextMenuStrip();menu.Items.Add("主屏",null,(o,e)=>Open(""));menu.Items.Add("设置",null,(o,e)=>Open("settings.html"));menu.Items.Add("退出",null,(o,e)=>ExitThread());icon=new NotifyIcon{Icon=SystemIcons.Application,Text="CTCS DMI 网页桥接",Visible=true,ContextMenuStrip=menu};icon.DoubleClick+=(o,e)=>Open("");Open("");}
    void Open(string path){Process.Start("http://127.0.0.1:"+bridge.Port+"/"+path);}
    protected override void ExitThreadCore(){icon.Visible=false;icon.Dispose();bridge.Dispose();base.ExitThreadCore();}
}
static class Program {
    static bool ExistingBridge(int port){try{var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+"/api/bootstrap");request.Proxy=null;request.Timeout=1200;request.ReadWriteTimeout=1200;request.AllowAutoRedirect=false;using(var response=request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream())){var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());object product,id;return data.TryGetValue("product",out product)&&(string)product=="TSW7-DMI-Web"&&data.TryGetValue("bridgeId",out id)&&id is string&&((string)id).Length==32;}}catch{return false;}}
    static void OpenExisting(int port,bool headless){if(!headless)Process.Start("http://127.0.0.1:"+port+"/");}
    [STAThread] static int Main(string[] args){int port=31271;foreach(string arg in args)if(arg.StartsWith("--port=")){if(!Int32.TryParse(arg.Substring(7),out port)||port<1024||port>65535)return 1;}
        bool headless=args.Contains("--headless");try{if(ExistingBridge(port)){OpenExisting(port,headless);return 0;}using(var bridge=new Bridge(AppDomain.CurrentDomain.BaseDirectory,port,!args.Contains("--local-only"))){try{bridge.Start();}catch(SocketException){if(ExistingBridge(port)){OpenExisting(port,headless);return 0;}throw;}if(headless){while(true)Thread.Sleep(1000);}else{Application.EnableVisualStyles();Application.Run(new Tray(bridge));}}return 0;}
        catch(Exception e){if(headless){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bridge-error.txt"),e.ToString());}else MessageBox.Show(e.Message+"\n若已启动，请从系统托盘打开主屏或设置。","DMI 网页桥接",MessageBoxButtons.OK,MessageBoxIcon.Information);return 1;}
    }
}
}

using System; using System.Collections; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Linq; using System.Net; using System.Text; using System.Web.Script.Serialization;
namespace TSW7DMI {
public class Settings {
    public string BaseUrl = "http://127.0.0.1:31270";
    public string KeyFile = "";
    public string SpeedEndpoint = "CurrentDrivableActor.Function.HUD_GetSpeed";
    public string SpeedField = "";
    public string SpeedUnit = "m/s";
    public string AidEndpoint = "DriverAid.Data";
    public string AidSpeedUnit = "m/s";
    public string AidDistanceUnit = "cm";
    public string TrackEndpoint = "DriverAid.TrackData";
    public bool AutoConnect = true;
    public string Level = "CTCS-3";
    public double Deceleration = 0.65;
    public double ReactionSeconds = 3;
    public double SafetyDistance = 30;
    public static Settings Load() {
        try { var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json"),Encoding.UTF8)); if(s==null||Double.IsNaN(s.Deceleration)||Double.IsInfinity(s.Deceleration)||s.Deceleration<0.1||s.Deceleration>2||Double.IsNaN(s.ReactionSeconds)||s.ReactionSeconds<0||s.ReactionSeconds>20||Double.IsNaN(s.SafetyDistance)||s.SafetyDistance<0||s.SafetyDistance>500) return new Settings();return s; }
        catch { return new Settings(); }
    }
    public void Save() { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json"),new JavaScriptSerializer().Serialize(this),new UTF8Encoding(true)); }
}
public class Frame {
    public double? Speed, Limit, Target, Distance;
    public DateTime Timestamp;
    public string Source = "TSW 7", Error = "", RawSpeed = "", RawAid = "", Station = "";
    public object[] Grades = new object[0];
    public bool Live, Fresh;
    public object[] Signals = new object[0];
    public string Signal = "", SignalId = "", RouteId = "", Vehicle = "", Service = "", GameTime = "";
    public double? SignalDistance, Acceleration, BrakeInput, BrakeOutput;
    public string VehicleId="";public bool BrakeSupported,BrakeEmergency,BrakeService;public string BrakeControl="";
    public bool? GameAdvancing;
    public double? StationDistance,PlatformLength,CabPermitted,CabWarning;
    public string Platform="",PlatformSide="";public object PlatformInfo;public object[] LineConditions=new object[0];
    public bool CabActive;
    public bool? CabActivated;
    public string CabActivationSource="CurrentDrivableActor.Function.IsCabActive";
    public string CabActivationReason="等待司机室激活状态";
    public string[] AvailableSystems=new string[0];
    public object[] Stations=new object[0],SpeedPoints=new object[0];
}
public static class JsonData {
    public static object Parse(string json) {
        var s=new JavaScriptSerializer {MaxJsonLength=8*1024*1024}; var root=s.DeserializeObject(json);
        var d=root as Dictionary<string,object>;
        if(d!=null) { object result; if(d.TryGetValue("Result",out result) && result!=null && !String.Equals(result.ToString(),"Success",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("游戏返回："+result); }
        return root;
    }
    public static object At(object root,string path) {
        if(String.IsNullOrWhiteSpace(path)) return root;
        object cur=root;
        foreach(string part in path.Split('.')) {
            var d=cur as Dictionary<string,object>;
            if(d!=null) { var key=d.Keys.FirstOrDefault(k=>String.Equals(k,part,StringComparison.OrdinalIgnoreCase)); if(key==null)return null;cur=d[key]; }
            else {var a=cur as object[];int n;if(a!=null){if(!Int32.TryParse(part,out n)||n<0||n>=a.Length)return null;cur=a[n];}else{if(cur==null)return null;var prop=cur.GetType().GetProperty(part,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.IgnoreCase);if(prop==null)return null;cur=prop.GetValue(cur,null);}}
        } return cur;
    }
    public static double? Number(object v) {
        if(v==null||v is bool)return null;
        var d=v as Dictionary<string,object>;
        if(d!=null) { foreach(string key in new[]{"value","Value","ReturnValue","Speed"}) { if(d.ContainsKey(key)){var n=Number(d[key]);if(n.HasValue)return n;} } return null; }
        double result;if(Double.TryParse(Convert.ToString(v,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out result)&& !Double.IsNaN(result)&&!Double.IsInfinity(result))return result;return null;
    }
    public static bool? BooleanResult(object values){
        foreach(string field in new[]{"ReturnValue","bReturnValue","bActive","bIsCabActive","IsCabActive","IsActive?","IsActive","Value","Result"}){object value=At(values,field);if(value is bool)return (bool)value;}
        return null;
    }
    public static bool? ReverserForward(object display,double? input,double? output,bool inserted=false){
        var labels=At(display,"namedValues") as object[];if(labels==null)return null;
        foreach(var n in labels){string source=Convert.ToString(At(n,"valueSource"));double? value=source=="Input"?input:source=="Output"?output:null;
            var lo=Number(At(n,"valueRange.min"));var hi=Number(At(n,"valueRange.max"));if(!value.HasValue||Double.IsNaN(value.Value)||Double.IsInfinity(value.Value)||!lo.HasValue||!hi.HasValue||value<lo||value>hi)continue;
            string label=Convert.ToString(At(n,"displayName")).Trim().ToLowerInvariant();
            if(new[]{"前牵位","向前","前进","forward","vorwärts","vorwaerts"}.Contains(label))return true;
            if(new[]{"关闭","off"}.Contains(label))return false;
            if(new[]{"后牵位","向后","后退","空挡","空档","neutral","reverse","backward","rückwärts","rueckwaerts"}.Contains(label))return inserted;
        }return null;
    }
    static void Leaves(object o,List<double> numbers) {
        var d=o as Dictionary<string,object>;if(d!=null){foreach(var pair in d)Leaves(pair.Value,numbers);return;}
        var a=o as object[];if(a!=null){foreach(var v in a)Leaves(v,numbers);return;}
        var n=Number(o);if(n.HasValue)numbers.Add(n.Value);
    }
    public static double Speed(object root,string field) {
        double? n=null;
        if(!String.IsNullOrWhiteSpace(field))n=Number(At(root,field));
        else {
            foreach(string p in new[]{"Values.Speed","Values.speed","Values.ReturnValue","Values.Value","Values.HUD_GetSpeed","Value","ReturnValue","Speed"}){n=Number(At(root,p));if(n.HasValue)break;}
            if(!n.HasValue) { var nums=new List<double>();var values=At(root,"Values")??root;Leaves(values,nums);if(nums.Count==1)n=nums[0]; }
        }
        if(!n.HasValue)throw new InvalidDataException("无法唯一识别速度字段，请在接口诊断查看响应，并配置速度 JSON 字段。");
        return n.Value;
    }
    public static double ToKmh(double v,string unit) {return unit=="km/h"?v:unit=="mph"?v*1.609344:unit=="cm/s"?v*0.036:v*3.6;}
    public static double ToMetres(double v,string unit){return unit=="cm"?v/100:unit=="km"?v*1000:v;}
    public static void Aid(Frame f,object root,Settings s) {
        var v=At(root,"Values")??root;
        var lim=Number(At(v,"speedLimit"));
        if(lim.HasValue&&lim.Value>=0&&ToKmh(lim.Value,s.AidSpeedUnit)<=1000)f.Limit=ToKmh(lim.Value,s.AidSpeedUnit);
        // In TSW 7, the current limit can be valid while no upcoming speed-limit point is seen.
        var seen=At(v,"speedLimitSeen");bool nextSeen=!(seen is bool)||((bool)seen);
        var target=Number(At(v,"nextSpeedLimit"));var dist=Number(At(v,"distanceToNextSpeedLimit"));
        if(nextSeen&&target.HasValue&&target.Value>=0&&ToKmh(target.Value,s.AidSpeedUnit)<=1000&&dist.HasValue&&dist.Value>=0){f.Target=ToKmh(target.Value,s.AidSpeedUnit);f.Distance=ToMetres(dist.Value,s.AidDistanceUnit);}
        var signals=new List<object>();
        var signalSeen=At(v,"signalSeen");var sd=Number(At(v,"distanceToSignal"));
        if(signalSeen is bool&&(bool)signalSeen&&sd.HasValue&&sd>=0&&sd<1e9){f.Signal=Convert.ToString(At(v,"signalAspectClass"));f.SignalId=Convert.ToString(At(v,"signalPropertyGuid"));f.SignalDistance=ToMetres(sd.Value,s.AidDistanceUnit);signals.Add(new{aspect=f.Signal,distance=f.SignalDistance.Value});}
        var ahead=At(v,"nextSignals") as object[];
        if(ahead!=null)foreach(var item in ahead){var d=Number(At(item,"distanceToNextSignal"));var aspect=At(item,"value") as string;if(d.HasValue&&d>=0&&d<1e9&&aspect!=null&&(!f.SignalDistance.HasValue||Math.Abs(ToMetres(d.Value,s.AidDistanceUnit)-f.SignalDistance.Value)>2))signals.Add(new{aspect=aspect,distance=ToMetres(d.Value,s.AidDistanceUnit)});}
        f.Signals=signals.ToArray();
        var points=new List<object>();var speeds=At(v,"nextSpeedLimits") as object[];
        if(speeds!=null)foreach(var point in speeds){var d=Number(At(point,"distanceToNextSpeedLimit"));var value=Number(At(point,"value"));if(d.HasValue&&d>=0&&value.HasValue&&value>=0&&ToKmh(value.Value,s.AidSpeedUnit)<=1000)points.Add(new{distance=ToMetres(d.Value,s.AidDistanceUnit),speed=ToKmh(value.Value,s.AidSpeedUnit)});}
        f.SpeedPoints=points.ToArray();
    }
    public static void Track(Frame f,object root) {
        var v=At(root,"Values")??root;
        var stations=At(v,"stations") as object[];double nearest=Double.MaxValue;var stationList=new List<object>();
        f.PlatformInfo=null;double sideNearest=Double.MaxValue;
        if(stations!=null)foreach(var station in stations){var distance=Number(At(station,"distanceToStationCM"));var length=Number(At(station,"platformLength"));string side=Convert.ToString(At(station,"platformSide"));side=side.Equals("Left",StringComparison.OrdinalIgnoreCase)?"左侧":side.Equals("Right",StringComparison.OrdinalIgnoreCase)?"右侧":side.Equals("Both",StringComparison.OrdinalIgnoreCase)?"两侧":"";if(!distance.HasValue||side.Length==0)continue;bool range=length.HasValue&&length>0&&length<=1000000&&distance<=0&&distance>=-length; if(distance<0&&!range)continue;double rank=range?-1:distance.Value;if(rank>=sideNearest)continue;sideNearest=rank;f.PlatformInfo=new{id=Convert.ToString(At(station,"stationName"))+"|"+Convert.ToString(At(station,"platformNumber")??At(station,"platformName")),side=side,inRange=range,source="TSW station distance / platform length"};}
        var conditions=At(v,"lineConditions") as object[];var known=new[]{"桥梁","车站","隧道","临时限速区","分相区"};var clean=new List<object>();if(conditions!=null)foreach(var item in conditions){string type=Convert.ToString(At(item,"type"));var distance=Number(At(item,"distance"));var length=Number(At(item,"length"));if(known.Contains(type)&&distance.HasValue&&length.HasValue&&length>=0&&distance+length>=0)clean.Add(new{type=type,distance=distance.Value,length=length.Value});}f.LineConditions=clean.ToArray();
        if(stations!=null)foreach(var station in stations){var distance=Number(At(station,"distanceToStationCM"));var name=At(station,"stationName") as string;if(!distance.HasValue||distance<0||String.IsNullOrWhiteSpace(name))continue;
            string platform=Convert.ToString(At(station,"platformNumber")??At(station,"platformName"));string side=Convert.ToString(At(station,"platformSide"));var length=Number(At(station,"platformLength"));if(length<0||length>1000000)length=null;
            side=side.Equals("Left",StringComparison.OrdinalIgnoreCase)?"左侧":side.Equals("Right",StringComparison.OrdinalIgnoreCase)?"右侧":side.Equals("Both",StringComparison.OrdinalIgnoreCase)?"两侧":"";
            stationList.Add(new{name=name,distance=distance.Value/100,platform=platform,side=side,length=length/100});
            if(distance<nearest){nearest=distance.Value;f.Station=name;f.StationDistance=distance.Value/100;f.Platform=platform;f.PlatformSide=side;f.PlatformLength=length/100;}}
        var uniqueStations=new List<object>();foreach(var station in stationList.OrderBy(p=>Number(At(p,"distance"))))if(!uniqueStations.Any(p=>Object.Equals(At(p,"name"),At(station,"name"))&&Object.Equals(At(p,"platform"),At(station,"platform"))&&Math.Abs(Number(At(p,"distance")).Value-Number(At(station,"distance")).Value)<500))uniqueStations.Add(station);
        f.Stations=uniqueStations.Take(12).ToArray();
        var heights=At(v,"trackHeights") as object[];var points=new List<Tuple<double,double>>();
        if(heights!=null)foreach(var point in heights){var d=Number(At(point,"distanceToHeight"));var h=Number(At(point,"height"));if(d.HasValue&&h.HasValue&&d>=0)points.Add(Tuple.Create(d.Value,h.Value));}
        points=points.OrderBy(p=>p.Item1).ToList();var grades=new List<object>();
        for(int i=1;i<points.Count;i++){double length=points[i].Item1-points[i-1].Item1;if(length<=0)continue;double grade=1000*(points[i].Item2-points[i-1].Item2)/length;if(Math.Abs(grade)<=100)grades.Add(new{distance=points[i-1].Item1/100,length=length/100,grade=Math.Round(grade,1)});}
        f.Grades=grades.ToArray();
    }
}
public class ApiClient {
    readonly string url,key;
    public string LastSpeedResponse="",LastAidResponse="";
    DateTime metadataAt=DateTime.MinValue;
    string vehicle="",routeId="",service="",gameTime="";
    string previousGameTime="";DateTime clockChangedAt=DateTime.MinValue;
    string systemsVehicle="";string[] availableSystems=new string[0];
    public Func<string,DriveMap> MapResolver;
    readonly object automaticGate=new object();DriveMap automaticMap;string attemptedVehicle="";DateTime attemptedAt;
    DriveMap Mapping(string name){var configured=MapResolver==null?DriveMap.Builtin(name):MapResolver(name);if(configured!=null)return configured;lock(automaticGate){if(automaticMap!=null&&automaticMap.vehicle==name)return automaticMap;if(attemptedVehicle==name&&(DateTime.UtcNow-attemptedAt).TotalSeconds<10)return null;attemptedVehicle=name;attemptedAt=DateTime.UtcNow;automaticMap=null;
        try{var splitMap=DiscoverIce(name);if(splitMap!=null){automaticMap=splitMap;return splitMap;}}catch{}
        string[] candidates;try{candidates=LeverControls();}catch{return null;}foreach(string control in candidates.Take(12)){try{if(Convert.ToString(JsonData.At(Values(control+".Property.InputIdentifier"),"identifier"))!="Throttle")continue;
            var endpoints=JsonData.At(JsonData.Parse(Get("/list/"+String.Join("/",control.Split('/').Select(Uri.EscapeDataString)))),"Endpoints") as object[];
            if(endpoints==null||!new[]{"InputValue","Interacting"}.All(k=>endpoints.Any(e=>Convert.ToString(JsonData.At(e,"Name"))==k&&Object.Equals(JsonData.At(e,"Writable"),true))))continue;
            var min=Numeric(control+".Function.GetMinimumInputValue","ReturnValue");var max=Numeric(control+".Function.GetMaximumInputValue","ReturnValue");var lo=Numeric(control+".Function.GetMinimumOutputValue","ReturnValue");var hi=Numeric(control+".Function.GetMaximumOutputValue","ReturnValue");if(!min.HasValue||!max.HasValue||!lo.HasValue||!hi.HasValue)continue;
            var map=DriveMap.Discover(name,control,min.Value,max.Value,lo.Value,hi.Value,Values(control+".Property.DisplayInfo"));if(map!=null){automaticMap=map;return map;}
        }catch{}}return null;}}
    DriveMap DiscoverIce(string name){
        if(!System.Text.RegularExpressions.Regex.IsMatch(name,"ICE|BR_?40[136]|BR_?412",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return null;
        const string power="CurrentDrivableActor/Throttle",brake="CurrentDrivableActor/TrainBrake";
        foreach(string c in new[]{power,brake}){var endpoints=JsonData.At(JsonData.Parse(Get("/list/"+c)),"Endpoints") as object[];if(endpoints==null||!new[]{"InputValue","Interacting"}.All(k=>endpoints.Any(e=>Convert.ToString(JsonData.At(e,"Name"))==k&&Object.Equals(JsonData.At(e,"Writable"),true))))return null;}
        if(Convert.ToString(JsonData.At(Values(power+".Property.InputIdentifier"),"identifier"))!="Throttle"||Convert.ToString(JsonData.At(Values(brake+".Property.InputIdentifier"),"identifier"))!="AutomaticBrake")return null;
        if(Numeric(power+".Function.GetMinimumInputValue","ReturnValue")!=0||Numeric(power+".Function.GetMaximumInputValue","ReturnValue")!=1||Numeric(brake+".Function.GetMaximumInputValue","ReturnValue")!=1||Numeric(brake+".Function.GetMaximumOutputValue","ReturnValue")!=10)return null;
        var min=Numeric(brake+".Function.GetMinimumInputValue","ReturnValue");if(!min.HasValue)return null;
        var labels=JsonData.At(Values(brake+".Property.DisplayInfo"),"namedValues") as object[];if(labels==null)return null;
        Func<double,string[],bool> named=(value,words)=>labels.Any(n=>Convert.ToString(JsonData.At(n,"valueSource"))=="Output"&&JsonData.Number(JsonData.At(n,"valueRange.min"))<=value&&JsonData.Number(JsonData.At(n,"valueRange.max"))>=value&&words.Any(w=>Convert.ToString(JsonData.At(n,"displayName")).ToLowerInvariant().Contains(w)));
        bool outputLabels=named(0,new[]{"缓解","release"})&&named(9,new[]{"全制动","full service","full brake"})&&named(10,new[]{"紧急","emergency"});
        Func<double,string[],bool> inputNamed=(value,words)=>labels.Any(n=>Convert.ToString(JsonData.At(n,"valueSource"))=="Input"&&JsonData.Number(JsonData.At(n,"valueRange.min"))<=value&&JsonData.Number(JsonData.At(n,"valueRange.max"))>=value&&words.Contains(Convert.ToString(JsonData.At(n,"displayName")).Trim().ToLowerInvariant()));
        bool inputLabels=name.IndexOf("BR412",StringComparison.OrdinalIgnoreCase)>=0&&min.Value==0&&inputNamed(.1,new[]{"行驶","running","release"})&&inputNamed(.83,new[]{"全制动","full service","full brake"})&&inputNamed(1,new[]{"紧急制动","emergency","emergency brake"});
        if(!outputLabels&&!inputLabels)return null;
        var map=new DriveMap{vehicle=name,split=true,control=brake,throttleControl=power,minimum=min.Value,maximum=1,neutral=0,power=1,service=.9,emergency=1,neutralOutput=0,serviceOutput=9,emergencyOutput=10};return map.Valid()?map:null;
    }
    object SetSplitDrive(DriveMap map,double throttle,double brake,bool emergency,string vehicle,string cab){
        // Drop traction before touching the brake. Never power with unconfirmed release.
        ValidatedMap(vehicle,cab);
        if(emergency||brake>0||throttle==0||Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue")!=0){WriteLever(map.throttleControl+".InputValue",0);
        var cut=Numeric(map.throttleControl+".InputValue","InputValue");if(!cut.HasValue||Math.Abs(cut.Value)>.015)throw new InvalidOperationException("ICE 牵引切断未确认，未继续操纵");}
        double target=emergency?10:brake>0?Math.Max(1,Math.Round(9*brake/100)):0;
        var result=SetAutomaticDrive(map,0,target/9*100,emergency,vehicle,cab);
        if(!Object.Equals(JsonData.At(result,"matched"),true))return result;
        if(!emergency&&brake==0&&throttle>0){ValidatedMap(vehicle,cab);WriteLever(map.throttleControl+".InputValue",throttle/100);var input=Numeric(map.throttleControl+".InputValue","InputValue");if(!input.HasValue||Math.Abs(input.Value-throttle/100)>.02)throw new InvalidOperationException("ICE 牵引手柄回读不匹配");}
        return result;
    }
    public const string Class170Brake="CurrentDrivableActor/IrregularLever_ThrottleBrake.InputValue";
    public ApiClient(Settings s,string secret) {
        Uri u;if(!Uri.TryCreate(s.BaseUrl,UriKind.Absolute,out u)||u.Scheme!="http"|| !u.IsLoopback||!String.IsNullOrEmpty(u.UserInfo)||u.AbsolutePath!="/"||!String.IsNullOrEmpty(u.Query)||!String.IsNullOrEmpty(u.Fragment))throw new ArgumentException("本版本接口地址须为本机 HTTP，例如 http://127.0.0.1:31270");
        url=s.BaseUrl.TrimEnd('/');key=secret.Trim();if(key.Length==0)throw new ArgumentException("请指定 CommAPIKey.txt，或在密钥框输入接口密钥。");
    }
    public string Get(string route) {
        if(!route.StartsWith("/",StringComparison.Ordinal)||route.Contains(".."))throw new ArgumentException("接口路径应以 / 开头。");
        var req=(HttpWebRequest)WebRequest.Create(url+route);req.Method="GET";req.Proxy=null;req.Timeout=1200;req.ReadWriteTimeout=1200;req.AllowAutoRedirect=false;req.Headers["DTGCommKey"]=key;
        try {using(var response=(HttpWebResponse)req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){var text=reader.ReadToEnd();if(text.Length>8*1024*1024)throw new InvalidDataException("响应过大");return text;}}
        catch(WebException e) {var r=e.Response as HttpWebResponse; if(r!=null){int code=(int)r.StatusCode;r.Dispose();if(code==401||code==403)throw new InvalidOperationException("鉴权失败：重新选择 TSW 7 的 CommAPIKey.txt。");throw new InvalidOperationException("HTTP "+code+"：检查端点或进入驾驶室后重试。");}throw new InvalidOperationException("无法连接 TSW 7：确认游戏以 -HTTPAPI 启动，端口 31270 已开启。",e);}
    }
    public static string EndpointRoute(string endpoint) {return "/get/"+Uri.EscapeDataString(endpoint.Trim());}
    object Values(string endpoint){var root=JsonData.Parse(Get(EndpointRoute(endpoint)));return JsonData.At(root,"Values")??root;}
    double? Numeric(string endpoint,string field){return JsonData.Number(JsonData.At(Values(endpoint),field));}
    public bool? CabActivation(){string source;return CabActivation(out source);}
    bool? CabActivation(out string source){
        source="CurrentDrivableActor.Function.IsCabActive";
        try{var result=JsonData.BooleanResult(Values(source));if(result.HasValue)return result;}catch{}
        // Class 220 exposes this spelling and returns bIsCabActive.
        source="CurrentDrivableActor.Function.GetIsCabActive";
        try{var result=JsonData.BooleanResult(Values(source));if(result.HasValue)return result;}catch{}
        // Class 484 does not expose IsCabActive. Its key and MU-master endpoints
        // were read from the live vehicle's own API inventory and verified together.
        string actual=Convert.ToString(JsonData.At(Values("CurrentDrivableActor.ObjectClass"),"ObjectClass"));
        if(actual.StartsWith("RVM_NYH_MNR_M7a-",StringComparison.OrdinalIgnoreCase)){
            source="M7A 换向器投入状态（关闭档以外的已知档位）";
            try{return JsonData.ReverserForward(Values("CurrentDrivableActor/Reverser.Property.DisplayInfo"),Numeric("CurrentDrivableActor/Reverser.InputValue","InputValue"),Numeric("CurrentDrivableActor/Reverser.Function.GetCurrentOutputValue","ReturnValue"),true);}catch{return null;}
        }
        if(System.Text.RegularExpressions.Regex.IsMatch(actual,@"_DB_|ICE|_BR\d",System.Text.RegularExpressions.RegexOptions.IgnoreCase)){
            source="德铁换向器向前档（游戏未提供司机室激活状态）";
            try{return JsonData.ReverserForward(Values("CurrentDrivableActor/Reverser.Property.DisplayInfo"),Numeric("CurrentDrivableActor/Reverser.InputValue","InputValue"),Numeric("CurrentDrivableActor/Reverser.Function.GetCurrentOutputValue","ReturnValue"));}catch{return null;}
        }
        if(!actual.StartsWith("RVM_LDN_Class484_",StringComparison.Ordinal))return null;
        source="Class 484: KeySwitchOn + MasterKeyOn + MUMaster";
        bool? key=JsonData.BooleanResult(Values("CurrentDrivableActor.Property.KeySwitchOn"));
        bool? masterKey=JsonData.At(Values("CurrentDrivableActor.Function.GetMasterKeyState(Bool)"),"MasterKeyOn") as bool?;
        bool? master=JsonData.At(Values("CurrentDrivableActor.Function.MTD_IsMUMaster"),"Result") as bool?;
        if(key==false||masterKey==false||master==false)return false;
        return key==true&&masterKey==true&&master==true?(bool?)true:null;
    }
    void RequireActiveCab(){if(CabActivation()!=true)throw new InvalidOperationException("司机室未激活或状态无法验证，禁止操纵写入。");}
    public object SetBrake(double percent,bool emergency,string expectedVehicle,string expectedCab=""){return SetDrive(0,percent,emergency,expectedVehicle,expectedCab);}
    DriveMap ValidatedMap(string expectedVehicle,string expectedCab){
        RequireActiveCab();string actual=Convert.ToString(JsonData.At(Values("CurrentDrivableActor.ObjectClass"),"ObjectClass"));var map=Mapping(actual);
        if(expectedCab.Length>0&&Convert.ToString(JsonData.At(Values("CurrentDrivableActor.ObjectName"),"ObjectName"))!=expectedCab)throw new InvalidOperationException("司机室实例已改变，未发送操纵。");
        if(actual!=expectedVehicle||map==null||!map.Valid())throw new InvalidOperationException("车辆已改变或尚未完成车型手柄校准。");
        if(map.vehicle.StartsWith("RVM_BR_Class170_",StringComparison.Ordinal)&&map.control=="CurrentDrivableActor/IrregularLever_ThrottleBrake"){
            if(!Object.Equals(JsonData.At(Values(map.control+".Property.bCustomOutputValueMapping"),"Value"),false)||Numeric(map.control+".Function.GetMinimumOutputValue","ReturnValue")!=-4||Numeric(map.control+".Function.GetMaximumOutputValue","ReturnValue")!=7)throw new InvalidOperationException("内置车型档位映射与游戏不一致。");
        }else if(Numeric(map.control+".Function.GetMinimumInputValue","ReturnValue")!=map.minimum||Numeric(map.control+".Function.GetMaximumInputValue","ReturnValue")!=map.maximum)throw new InvalidOperationException("车型手柄范围已改变，自动适配已停止。");
        if(map.vehicle.StartsWith("RVM_GOB_Class710_",StringComparison.Ordinal)&&map.control=="CurrentDrivableActor/TractionBrakeControl"){
            if(!Object.Equals(JsonData.At(Values(map.control+".Property.bCustomOutputValueMapping"),"Value"),false)||Numeric(map.control+".Function.GetMinimumOutputValue","ReturnValue")!=-4||Numeric(map.control+".Function.GetMaximumOutputValue","ReturnValue")!=1||Numeric(map.control+".Function.GetNotchCount","ReturnValue")!=5)throw new InvalidOperationException("Class 710 游戏控件与内置适配不一致，未写入操纵。");
        }
        if(map.split&&(Numeric(map.throttleControl+".Function.GetMinimumInputValue","ReturnValue")!=0||Numeric(map.throttleControl+".Function.GetMaximumInputValue","ReturnValue")!=1||Numeric(map.control+".Function.GetMaximumOutputValue","ReturnValue")!=10))throw new InvalidOperationException("ICE 双手柄范围已改变");
        return map;
    }
    public string[] LeverControls(){var nodes=JsonData.At(JsonData.Parse(Get("/list/CurrentDrivableActor")),"Nodes") as object[];if(nodes==null)return new string[0];return nodes.Select(n=>Convert.ToString(JsonData.At(n,"Name"))).Where(n=>System.Text.RegularExpressions.Regex.IsMatch(n,@"^[A-Za-z0-9_() -]{1,100}$")&&(n.IndexOf("Power",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("Throttle",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("MasterController",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("CombinedHandle",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("TractionBrakeControl",StringComparison.OrdinalIgnoreCase)>=0)).Select(n=>"CurrentDrivableActor/"+n).ToArray();}
    public LeverCapture CaptureLever(string control){
        RequireActiveCab();if(!LeverControls().Contains(control))throw new ArgumentException("请选择当前车辆实际存在的组合手柄。");
        var endpoints=JsonData.At(JsonData.Parse(Get("/list/"+String.Join("/",control.Split('/').Select(Uri.EscapeDataString)))),"Endpoints") as object[];
        if(endpoints==null||!endpoints.Any(e=>Convert.ToString(JsonData.At(e,"Name"))=="InputValue"&&Object.Equals(JsonData.At(e,"Writable"),true)))throw new ArgumentException("此控件没有可写手柄输入。");
        var input=Numeric(control+".InputValue","InputValue");var output=Numeric(control+".Function.GetCurrentOutputValue","ReturnValue");
        if(!input.HasValue||!output.HasValue)throw new ArgumentException("手柄实际输入或输出无法读取。");return new LeverCapture{input=input.Value,output=output.Value};
    }
    public DriveMap FinishCalibration(CalibrationDraft draft){
        if(!new[]{"neutral","power","service","emergency"}.All(draft.points.ContainsKey))throw new ArgumentException("请先记录全部四个档位。");
        var min=Numeric(draft.control+".Function.GetMinimumInputValue","ReturnValue");var max=Numeric(draft.control+".Function.GetMaximumInputValue","ReturnValue");if(!min.HasValue||!max.HasValue)throw new ArgumentException("无法验证手柄输入范围。");
        var m=new DriveMap{vehicle=draft.vehicle,control=draft.control,minimum=min.Value,maximum=max.Value,neutral=draft.points["neutral"].input,power=draft.points["power"].input,service=draft.points["service"].input,emergency=draft.points["emergency"].input,neutralOutput=draft.points["neutral"].output,powerOutput=draft.points["power"].output,serviceOutput=draft.points["service"].output,emergencyOutput=draft.points["emergency"].output};
        if(!m.Valid())throw new ArgumentException("档位关系不符合牵引／空挡／常用／紧急组合手柄，请重新记录；独立制动手柄暂不支持。");return m;
    }
    void PatchValue(string endpoint,double value){
        string path=String.Join("/",endpoint.Split('/').Select(Uri.EscapeDataString));
        var req=(HttpWebRequest)WebRequest.Create(url+"/set/"+path+"?Value="+value.ToString("F6",CultureInfo.InvariantCulture));req.Proxy=null;req.Method="PATCH";req.ContentLength=0;req.Timeout=1200;req.AllowAutoRedirect=false;req.Headers["DTGCommKey"]=key;
        using(var response=(HttpWebResponse)req.GetResponse())using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8)){JsonData.Parse(reader.ReadToEnd());}
    }
    void WriteLever(string endpoint,double value){
        string interaction=endpoint.Replace(".InputValue",".Interacting");
        PatchValue(interaction,1.0);
        try{PatchValue(endpoint,value);for(int attempt=0;attempt<6;attempt++){System.Threading.Thread.Sleep(60);var readback=Numeric(endpoint,"InputValue");if(readback.HasValue&&Math.Abs(readback.Value-value)<.015)break;}}
        finally{PatchValue(interaction,0.0);}
    }
    public object SetDrive(double throttle,double brake,bool emergency,string expectedVehicle,string expectedCab=""){
        var map=ValidatedMap(expectedVehicle,expectedCab);if(map.split)return SetSplitDrive(map,throttle,brake,emergency,expectedVehicle,expectedCab);if(map.automatic)return SetAutomaticDrive(map,throttle,brake,emergency,expectedVehicle,expectedCab);double value=map.Input(throttle,brake,emergency);string endpoint=map.control+".InputValue";
        var previousInput=Numeric(endpoint,"InputValue");
        WriteLever(endpoint,value);System.Threading.Thread.Sleep(100);
        var readback=Numeric(endpoint,"InputValue");var output=Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue");
        // Some TSW handles stop at the neutral gate until Interacting is released.
        // Retry once only for an observed crossing that actually stopped at neutral.
        if((previousInput.HasValue&&readback.HasValue&&(previousInput.Value-map.neutral)*(value-map.neutral)<0&&Math.Abs(readback.Value-map.neutral)<.02*(map.maximum-map.minimum)&&Math.Abs(value-map.neutral)>.03*(map.maximum-map.minimum))||(emergency&&output.HasValue&&Math.Abs(output.Value-map.serviceOutput)<.02)||(brake==0&&throttle==0&&!emergency&&previousInput.HasValue&&Math.Abs(previousInput.Value-map.emergency)<.02*(map.maximum-map.minimum)&&output.HasValue&&Math.Abs(output.Value-map.serviceOutput)<.02)){
            ValidatedMap(expectedVehicle,expectedCab);WriteLever(endpoint,value);System.Threading.Thread.Sleep(100);readback=Numeric(endpoint,"InputValue");output=Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue");
        }
        // Release may cross service and minimum-brake detents separately.
        // Retry only while measured braking progresses toward neutral; never accept
        // a residual brake output as a successful release.
        for(int gateAttempt=0;gateAttempt<2&&!emergency&&brake==0&&throttle==0&&map.IsBraking(output)&&!map.IsEmergency(output);gateAttempt++){
            double? prior=readback;ValidatedMap(expectedVehicle,expectedCab);WriteLever(endpoint,value);System.Threading.Thread.Sleep(100);readback=Numeric(endpoint,"InputValue");output=Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue");
            if(prior.HasValue&&readback.HasValue&&Math.Abs(prior.Value-readback.Value)<.001)break;
        }
        bool matched=readback.HasValue&&Math.Abs(readback.Value-value)<.06*(map.maximum-map.minimum)&&output.HasValue;
        if(emergency)matched&=map.IsEmergency(output);
        else if(brake>=99.9)matched&=output.HasValue&&Math.Abs(output.Value-map.serviceOutput)<.02;
        else if(brake==0&&throttle==0)matched&=output.HasValue&&Math.Abs(output.Value-map.neutralOutput)<.02;
        else if(brake==0&&throttle>=99.9)matched&=output.HasValue&&Math.Abs(output.Value-map.powerOutput)<.02;
        return new{accepted=true,matched=matched,input=value,requestedPercent=brake,emergency=emergency,readbackInput=readback,readbackOutput=output,message=matched?"游戏实际手柄已匹配；继续核对速度和加速度":"实际手柄回读不匹配，检查其他控制输入或车型校准"};
    }
    object SetAutomaticDrive(DriveMap map,double throttle,double brake,bool emergency,string vehicle,string cab){
        double target=emergency?map.emergencyOutput:brake>0?map.serviceOutput*brake/100:map.powerOutput*throttle/100;if(brake==0&&!emergency&&map.powerStep>0)target=Math.Round(target/map.powerStep)*map.powerStep;
        string endpoint=map.control+".InputValue";double low=map.minimum,high=map.maximum;double? input=Numeric(endpoint,"InputValue"),output=Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue");bool matched=false;
        for(int n=0;n<10;n++){if(output.HasValue&&Math.Abs(output.Value-target)<=.035){matched=true;break;}if(input.HasValue&&output.HasValue){if(output<target)low=Math.Max(low,input.Value);else high=Math.Min(high,input.Value);}double next=(low+high)/2;if(emergency)next=map.emergency;ValidatedMap(vehicle,cab);PatchValue(map.control+".Interacting",1);try{PatchValue(endpoint,next);}finally{PatchValue(map.control+".Interacting",0);}System.Threading.Thread.Sleep(100);input=Numeric(endpoint,"InputValue");output=Numeric(map.control+".Function.GetCurrentOutputValue","ReturnValue");}
        matched|=output.HasValue&&Math.Abs(output.Value-target)<=.035;
        return new{accepted=true,matched=matched,input=input,requestedPercent=brake,emergency=emergency,readbackInput=input,readbackOutput=output,targetOutput=target,message=matched?"自动识别控件，实际输出已匹配":"自动操纵输出未匹配，请检查游戏手柄状态"};
    }
    void Telemetry(Frame f){
        try{f.CabActivated=CabActivation(out f.CabActivationSource);f.CabActivationReason=f.CabActivated==true?"司机室已激活":f.CabActivated==false?"司机室未激活，请在游戏中投入司机室":"游戏未提供明确的司机室激活状态";}catch{f.CabActivationReason="司机室激活验证接口不可用，DMI 保持关闭";}
        try{f.Acceleration=Numeric("CurrentDrivableActor.Function.HUD_GetAcceleration","Acceleration (ms2)");}catch{}
        try{vehicle=Convert.ToString(JsonData.At(Values("CurrentDrivableActor.ObjectClass"),"ObjectClass"));}catch{vehicle="";}
        f.Vehicle=vehicle;try{f.VehicleId=Convert.ToString(JsonData.At(Values("CurrentDrivableActor.ObjectName"),"ObjectName"));}catch{}var driveMap=Mapping(vehicle);f.BrakeSupported=driveMap!=null&&driveMap.Valid();
        try{var cab=Values("CurrentDrivableActor.Function.HUD_GetMaxPermittedSpeed");var permitted=JsonData.Number(JsonData.At(cab,"MaxSpeed (ms)"));var warning=JsonData.Number(JsonData.At(cab,"WarningSpeed (ms)"));
            if(Object.Equals(JsonData.At(cab,"IsActive"),true)&&permitted.HasValue&&permitted>=0&&permitted<=1000/3.6){f.CabActive=true;f.CabPermitted=permitted*3.6;if(warning.HasValue&&warning>=permitted&&warning<=1000/3.6)f.CabWarning=warning*3.6;}}
        catch{}
        if(systemsVehicle!=vehicle){systemsVehicle=vehicle;availableSystems=new string[0];try{var nodes=JsonData.At(JsonData.Parse(Get("/list/CurrentDrivableActor")),"Nodes") as object[];if(nodes!=null){string names=String.Join(" ",nodes.Select(n=>Convert.ToString(JsonData.At(n,"Name")))).ToUpperInvariant();var systems=new List<string>();foreach(string key in new[]{"TVM","ETCS","LZB","PZB","KVB"})if(names.Contains(key))systems.Add(key=="TVM"?"TVM430":key);if(names.Contains("AWS")||names.Contains("TPWS"))systems.Add("AWS/TPWS");availableSystems=systems.ToArray();}}catch{}}
        f.AvailableSystems=availableSystems;
        if(f.BrakeSupported){try{f.BrakeControl=driveMap.control;f.BrakeInput=Numeric(driveMap.control+".InputValue","InputValue");f.BrakeOutput=Numeric(driveMap.control+".Function.GetCurrentOutputValue","ReturnValue");f.BrakeSupported=f.BrakeInput.HasValue&&f.BrakeOutput.HasValue;f.BrakeEmergency=driveMap.IsEmergency(f.BrakeOutput);f.BrakeService=driveMap.IsBraking(f.BrakeOutput)&&!f.BrakeEmergency;}catch{f.BrakeSupported=false;}}
        object clock=null;gameTime="";try{clock=Values("TimeOfDay.Data");gameTime=Convert.ToString(JsonData.At(clock,"LocalTimeISO8601"));}catch{}
        if((DateTime.UtcNow-metadataAt).TotalSeconds>3){metadataAt=DateTime.UtcNow;routeId=service="";
            try{var lat=JsonData.Number(JsonData.At(clock,"OriginLatitude"));var lon=JsonData.Number(JsonData.At(clock,"OriginLongitude"));if(lat.HasValue&&lon.HasValue)routeId="origin:"+lat.Value.ToString("F4",CultureInfo.InvariantCulture)+","+lon.Value.ToString("F4",CultureInfo.InvariantCulture);}catch{}
            try{service=Convert.ToString(JsonData.At(Values("DriverAid.PlayerInfo"),"currentServiceName"));if(service=="None")service="";}catch{}
        }
        if(gameTime!=""&&gameTime!=previousGameTime){previousGameTime=gameTime;clockChangedAt=DateTime.UtcNow;}
        f.GameAdvancing=gameTime==""?(bool?)null:(DateTime.UtcNow-clockChangedAt).TotalSeconds<2.5;
        f.RouteId=routeId;f.Service=service;f.GameTime=gameTime;
    }
    public Frame Read(Settings s) {
        var f=new Frame{Live=true,Source="TSW 7 实时",Timestamp=DateTime.UtcNow};
        f.RawSpeed=Get(EndpointRoute(s.SpeedEndpoint));LastSpeedResponse=f.RawSpeed;LastAidResponse="";var speed=Math.Abs(JsonData.ToKmh(JsonData.Speed(JsonData.Parse(f.RawSpeed),s.SpeedField),s.SpeedUnit));
        if(speed>1000)throw new InvalidDataException("速度超过 1000 km/h，请核对速度单位或 JSON 字段。");f.Speed=speed;
        if(!String.IsNullOrWhiteSpace(s.AidEndpoint)){try{f.RawAid=Get(EndpointRoute(s.AidEndpoint));LastAidResponse=f.RawAid;JsonData.Aid(f,JsonData.Parse(f.RawAid),s);}catch(Exception e){f.Error="速度有效；线路数据不可用："+e.Message;}}
        if(!String.IsNullOrWhiteSpace(s.TrackEndpoint)){try{JsonData.Track(f,JsonData.Parse(Get(EndpointRoute(s.TrackEndpoint))));}catch{ /* Optional route data does not invalidate the speed reading. */ }}
        Telemetry(f);f.Timestamp=DateTime.UtcNow;f.Fresh=true;return f;
    }
}

}

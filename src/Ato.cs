using System;using System.Linq;using TSW7DMI;
namespace CTCSWeb {
// Training ATO uses measured game motion; it never advances simulated telemetry.
public class AtoSession {
    public bool active,waiting,arrived,emergency,hasDestination;public string vehicle="",cab="",route="",station="",platform="",phase="off",message="ATO 未启用";
    public double kp=.65,ki=.12,kd=0,integralLimit=1.5,derivativeFilter=.5,pTerm,iTerm,dTerm,filteredDerivative;
    public bool allowCoast=true;public double coastBand=2;bool coasting;public object[] curve=new object[0];
    public static double TargetAt(double left,double allowed,double ceiling,double decel){if(left<=.5||allowed<=2)return 0;double v=Math.Max(0,Math.Sqrt(decel*decel*6.25+2*decel*Math.Max(0,left-.5))-decel*2.5)*3.6;if(left>.75&&v<1)v=1;v=Math.Min(v,1.5+Math.Max(0,left-15)*.6);return Math.Min(Math.Min(ceiling,Math.Max(0,allowed-2)),v);}
    public double TargetFor(double distance,double allowed){return hasDestination?TargetAt(remaining-distance,allowed,maxSpeed,deceleration):Math.Min(maxSpeed,Math.Max(0,allowed-2));}
    static bool DestinationAvailable(Frame f){return !String.IsNullOrWhiteSpace(f.Station)&&f.StationDistance.HasValue&&!Double.IsNaN(f.StationDistance.Value)&&!Double.IsInfinity(f.StationDistance.Value)&&f.StationDistance.Value>=0;}
    readonly System.Collections.Generic.HashSet<string> skipped=new System.Collections.Generic.HashSet<string>();public string skippedStation="";
    bool TryAcquire(Frame f){
        if(DestinationAvailable(f)&&!skipped.Contains(f.Station)){AcquireDestination(f);return true;}
        foreach(var s in f.Stations.OrderBy(s=>JsonData.Number(JsonData.At(s,"distance"))??Double.MaxValue)){
            string name=Convert.ToString(JsonData.At(s,"name")),pf=Convert.ToString(JsonData.At(s,"platform"));var distance=JsonData.Number(JsonData.At(s,"distance"));
            if(String.IsNullOrWhiteSpace(name)||!distance.HasValue||Double.IsNaN(distance.Value)||Double.IsInfinity(distance.Value)||distance<0||skipped.Contains(name))continue;
            AcquireDestination(new Frame{Station=name,Platform=pf,StationDistance=distance,PlatformLength=JsonData.Number(JsonData.At(s,"length"))});return true;
        }return false;
    }
    public void SkipStation(Frame f){if(!active||!hasDestination)throw new InvalidOperationException("ATO 没有正在执行的停车目标，无法跳站");skippedStation=station;skipped.Add(station);Cruise();TryAcquire(f);arrived=false;message="已跳过 "+skippedStation+(hasDestination?"，下一停车站 "+station:"，按限速运行并等待后续站点");}
    public double platformAdvance,targetOffset,platformEndMargin=10;
    void AcquireDestination(Frame f){hasDestination=true;station=f.Station;platform=f.Platform;platformAdvance=f.PlatformLength.HasValue&&f.PlatformLength.Value>10&&f.PlatformLength.Value<1500?Math.Max(0,f.PlatformLength.Value-platformEndMargin):0;targetOffset=offset+platformAdvance;remaining=f.StationDistance.Value+targetOffset;unobserved=0;markerPassed=false;integral=0;coasting=false;}
    void Cruise(){hasDestination=false;station=platform="";remaining=unobserved=integral=platformAdvance=targetOffset=0;markerPassed=coasting=false;}
    public void UpdateCurve(Frame f,Settings settings){
        if(!active||!f.Fresh){curve=new object[0];return;}
        var points=new System.Collections.Generic.List<Tuple<double,double>>();
        if(f.Target.HasValue&&f.Distance.HasValue)points.Add(Tuple.Create(f.Distance.Value,f.Target.Value));
        foreach(var p in f.SpeedPoints){var d=JsonData.Number(JsonData.At(p,"distance"));var v=JsonData.Number(JsonData.At(p,"speed"));if(d.HasValue&&v.HasValue)points.Add(Tuple.Create(d.Value,v.Value));}
        points=points.OrderBy(p=>p.Item1).ToList();
        var stops=f.Signals.Where(p=>Convert.ToString(JsonData.At(p,"aspect"))=="Stop").Select(p=>JsonData.Number(JsonData.At(p,"distance"))).Where(d=>d.HasValue&&d.Value>=0).Select(d=>d.Value).ToArray();
        double end=hasDestination?Math.Min(32000,Math.Max(0,remaining)):32000;if(stops.Length>0)end=Math.Min(end,stops.Min());
        var samples=new System.Collections.Generic.List<double>{0,end};for(int n=1;n<160;n++)samples.Add(end*n/160);foreach(var p in points)if(p.Item1>=0&&p.Item1<=end)samples.Add(p.Item1);
        var result=new System.Collections.Generic.List<object>();foreach(double distance in samples.Distinct().OrderBy(d=>d)){
            double limit=f.Limit??f.CabPermitted??0;foreach(var p in points)if(p.Item1<=distance&&p.Item1>0)limit=p.Item2;
            if(f.CabActive&&f.CabPermitted.HasValue)limit=Math.Min(limit,f.CabPermitted.Value);
            double allowed=limit;foreach(var p in points)if(p.Item1>=distance)allowed=Math.Min(allowed,Protection.Curve(limit,p.Item2,p.Item1-distance,settings));foreach(double stop in stops)if(stop>=distance)allowed=Math.Min(allowed,Protection.Curve(limit,0,stop-distance,settings));
            result.Add(new{distance=distance,speed=TargetFor(distance,allowed)});
        }curve=result.ToArray();
    }
    public double remaining,targetSpeed,throttle,brake,offset,maxSpeed=120,deceleration=.65;public DateTime lastFrame;
    DateTimeOffset previous;double previousSpeed,integral,unobserved,stalled;bool markerPassed;
    static double Clamp(double v,double lo,double hi){return Math.Max(lo,Math.Min(hi,v));}
    public static void ValidateOptions(double shift,double ceiling,double decel){if(new[]{shift,ceiling,decel}.Any(v=>Double.IsNaN(v)||Double.IsInfinity(v))||Math.Abs(shift)>500||ceiling<5||ceiling>350||decel<.2||decel>1.2)throw new ArgumentException("ATO 参数无效：偏移 ±500 m，最高速度 5–350 km/h，减速度 0.2–1.2 m/s²。");}
    public static void ValidatePid(double p,double i,double d,double limit,double filter){if(new[]{p,i,d,limit,filter}.Any(v=>Double.IsNaN(v)||Double.IsInfinity(v))||p<0||p>5||i<0||i>2||d<0||d>5||limit<0||limit>10||filter<.05||filter>5)throw new ArgumentException("PID 范围：P 0–5，I 0–2，D 0–5，积分上限 0–10，微分滤波 0.05–5 秒。");}
    public void ConfigurePid(double p,double i,double d,double limit,double filter){ValidatePid(p,i,d,limit,filter);kp=p;ki=i;kd=d;integralLimit=limit;derivativeFilter=filter;integral=filteredDerivative=pTerm=iTerm=dTerm=0;}
    public void Request(double shift,double ceiling,double decel){ValidateOptions(shift,ceiling,decel);offset=shift;maxSpeed=ceiling;deceleration=decel;waiting=true;active=arrived=emergency=false;station=vehicle=cab=route=platform="";hasDestination=false;remaining=targetSpeed=throttle=brake=0;phase="waiting";message="ATO 已激活，等待运行数据";}
    public void Start(Frame f,double shift,double ceiling,double decel){
        if(!f.Fresh||f.CabActivated!=true||!f.BrakeSupported||f.GameAdvancing!=true||!f.Speed.HasValue||f.VehicleId.Length==0)throw new InvalidOperationException("ATO 需要已激活司机室、推进中的游戏时间及可用的自动车型适配。");
        
        ValidateOptions(shift,ceiling,decel);
        if(!DateTimeOffset.TryParse(f.GameTime,out previous))throw new InvalidOperationException("缺少可靠游戏时钟。");
        coasting=false;curve=new object[0];vehicle=f.Vehicle;cab=f.VehicleId;route=f.RouteId;offset=shift;skipped.Clear();skippedStation="";Cruise();TryAcquire(f);maxSpeed=ceiling;deceleration=decel;previousSpeed=f.Speed.Value;lastFrame=f.Timestamp;integral=unobserved=stalled=filteredDerivative=pTerm=iTerm=dTerm=0;markerPassed=false;throttle=brake=0;active=true;waiting=false;arrived=emergency=false;phase="ready";message=hasDestination?"ATO 已启用，目标 "+station:"ATO 已启用 · 无目的地，按限速运行";
    }
    public void Stop(string reason){active=waiting=coasting=false;curve=new object[0];throttle=0;phase=arrived?"arrived":"off";message=reason;}
    public bool Step(Frame f,ProtectionState protection,bool penalty){
        if(!active)return false;
        if(!f.Fresh||f.CabActivated!=true||f.Vehicle!=vehicle||f.VehicleId!=cab||f.RouteId!=route)throw new InvalidOperationException("ATO 数据失效或司机室／线路已改变");
        if(f.GameAdvancing!=true)throw new InvalidOperationException("游戏时间未推进，ATO 已退出，恢复后需手动启用");
        if(!protection.permitted.HasValue||!f.Speed.HasValue)throw new InvalidOperationException("限速或速度数据丢失，ATO 已退出");
        if(f.Timestamp==lastFrame)return false;
        DateTimeOffset now;if(!DateTimeOffset.TryParse(f.GameTime,out now))throw new InvalidOperationException("游戏时钟不可用");
        double dt=(now-previous).TotalSeconds;if(dt==0){lastFrame=f.Timestamp;return false;}if(dt<0||dt>3)throw new InvalidOperationException("游戏时钟跳变，ATO 已退出");
        if(markerPassed&&platformAdvance>0&&unobserved>900)throw new InvalidOperationException("站台内停车距离推算超时，退出 ATO");
        double speed=f.Speed.Value,predicted=remaining-(previousSpeed+speed)/7.2*dt;unobserved+=dt;
        stalled=throttle>5&&speed<.3?stalled+dt:0;if(stalled>=20)throw new InvalidOperationException("牵引持续 20 秒但列车未移动，请检查换向器、车门和制动缓解");
        // Acquire an available destination while cruising; retain a locked marker through brief data gaps.
        bool acquired=false;if(!hasDestination)acquired=TryAcquire(f);
        if(hasDestination&&!acquired){
        double? measured=null;double discrepancy=Double.MaxValue;
        if(platformAdvance>0&&predicted-targetOffset<=0)markerPassed=true;
        foreach(var s in f.Stations){if(markerPassed&&platformAdvance>0)break;if(Convert.ToString(JsonData.At(s,"name"))!=station||Convert.ToString(JsonData.At(s,"platform"))!=platform)continue;var d=JsonData.Number(JsonData.At(s,"distance"));if(!d.HasValue)continue;double diff=Math.Abs(d.Value+targetOffset-predicted);if(diff<discrepancy){discrepancy=diff;measured=d.Value+targetOffset;}}
        if(measured.HasValue&&discrepancy>Math.Max(15,speed/3.6*2)&&platformAdvance>0&&predicted-targetOffset<=3){measured=null;markerPassed=true;}
        if(measured.HasValue){if(discrepancy>Math.Max(15,speed/3.6*2))throw new InvalidOperationException("车站距离发生跳变，ATO 已退出");remaining=measured.Value;unobserved=0;}else{if(predicted-targetOffset<=3&&remaining-targetOffset<=15)markerPassed=true;remaining=predicted;if(unobserved>3&&remaining>5&&!(markerPassed&&targetOffset>0&&(platformAdvance>0||unobserved<=120)))Cruise();}
        }
        filteredDerivative+=dt/(derivativeFilter+dt)*(-(speed-previousSpeed)/3.6/dt-filteredDerivative);
        previous=now;previousSpeed=speed;lastFrame=f.Timestamp;
        if(hasDestination&&remaining< -5){emergency=true;message="已越过停车目标，紧急制动并退出 ATO";phase="overrun";throttle=0;brake=100;return true;}
        if(penalty){emergency=true;phase="protection";message="已锁存惩罚制动，制动并退出 ATO";throttle=0;brake=100;return true;}
        if(hasDestination&&speed<.3&&remaining<=.75){arrived=true;phase="arrived";message="已在 "+station+" 停车，保持制动；不自动发车";throttle=0;brake=100;targetSpeed=0;return true;}
        targetSpeed=TargetFor(0,protection.permitted.Value);
        coasting=allowCoast&&targetSpeed>5&&speed>3&&speed<=targetSpeed+.5&&(coasting?speed>=targetSpeed-coastBand:speed>=targetSpeed-.5);
        double error=(targetSpeed-speed)/3.6;
        // Discard braking windup when the final approach needs traction again.
        // Keep signal and protection limits authoritative during any correction.
        if(hasDestination&&remaining> .75&&remaining<=15&&error>0&&integral<0)integral=0;
        double nextIntegral=Clamp(integral+error*dt,-integralLimit,integralLimit);
        pTerm=kp*error;dTerm=kd*filteredDerivative;
        double raw=pTerm+ki*nextIntegral+dTerm;
        if(!(raw>1&&error>0||raw< -1&&error<0))integral=nextIntegral;
        iTerm=ki*integral;
        double demand=Clamp(pTerm+iTerm+dTerm,-1,1);
        if(speed>targetSpeed+.5){double needed=Math.Max(.08,(speed*speed-targetSpeed*targetSpeed)/12.96/(2*Math.Max(3,hasDestination?Math.Min(remaining,40):40)));brake=Clamp(Math.Max(-demand,needed/deceleration)*100,10,100);throttle=0;}
        else if(coasting){throttle=brake=0;integral=0;iTerm=0;}
        else if(demand>.03&&targetSpeed>0){throttle=Clamp(demand*100,0,100);brake=0;}
        else{throttle=0;brake=targetSpeed==0?100:0;}
        phase=targetSpeed==0?"signal":throttle>0?"power":brake>0?"brake":"coast";
        message=targetSpeed==0?"停车信号／限速约束，等待放行":(phase=="coast"?"ATO 惰行至 ":phase=="brake"?"ATO 制动至 ":"ATO 牵引至 ")+(hasDestination?station:"前方 · 无目的地，按限速运行");
        return true;
    }
    public object Status(){return new{active=active,waiting=waiting,enabled=active||waiting,arrived=arrived,phase=phase,message=message,station=station,hasDestination=hasDestination,routing=hasDestination?"station":"limits",skippedStation=skippedStation,remaining=hasDestination?(double?)remaining:null,targetSpeed=targetSpeed,throttle=throttle,brake=brake,emergency=emergency,offset=offset,platformEndMargin=platformEndMargin,platformAdvance=platformAdvance,stopBasis=platformAdvance>0?"站台末端前 "+platformEndMargin+" m":"游戏车站标记",maxSpeed=maxSpeed,deceleration=deceleration,kp=kp,ki=ki,kd=kd,integralLimit=integralLimit,derivativeFilter=derivativeFilter,pTerm=pTerm,iTerm=iTerm,dTerm=dTerm,allowCoast=allowCoast,coastBand=coastBand,curve=curve};}
}
}

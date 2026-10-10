using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TSW7DMI;

namespace CTCSWeb {
public class JoystickDevice {
    public string id,name,backend="",profileKey="";public string[] axisNames=new string[0],buttonNames=new string[0];public double[] axes;public bool[] buttons,available;
}
public static class Joysticks {
    [StructLayout(LayoutKind.Sequential)]struct Info {public uint size,flags,x,y,z,r,u,v,buttons,buttonNumber,pov,res1,res2;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Caps {
        public ushort mid,pid;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;
        public uint xmin,xmax,ymin,ymax,zmin,zmax,buttons,pmin,pmax,rmin,rmax,umin,umax,vmin,vmax,caps,maxAxes,numAxes,maxButtons;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string reg;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string oem;
    }
    [DllImport("winmm.dll")]static extern uint joyGetNumDevs();
    [DllImport("winmm.dll")]static extern uint joyGetPosEx(uint id,ref Info info);
    [DllImport("winmm.dll",CharSet=CharSet.Unicode)]static extern uint joyGetDevCapsW(UIntPtr id,out Caps caps,uint size);
    static double Axis(uint value,uint min,uint max){return max>min?Math.Max(-1,Math.Min(1,2.0*((double)value-min)/(max-min)-1)):0;}
    public static JoystickDevice[] Read(){var result=new List<JoystickDevice>();
        for(uint id=0;id<Math.Min(joyGetNumDevs(),64);id++){
            var info=new Info{size=(uint)Marshal.SizeOf(typeof(Info)),flags=255};Caps cap;
            if(joyGetPosEx(id,ref info)!=0||joyGetDevCapsW((UIntPtr)id,out cap,(uint)Marshal.SizeOf(typeof(Caps)))!=0||(cap.numAxes==0&&cap.buttons==0))continue;
            result.Add(new JoystickDevice{id="winmm:"+id+":"+cap.mid+":"+cap.pid,name=cap.name,backend="WinMM",profileKey="winmm:"+cap.mid+":"+cap.pid+":"+cap.name,available=new[]{true,true,(cap.caps&1)!=0,(cap.caps&2)!=0,(cap.caps&4)!=0,(cap.caps&8)!=0},axes=new[]{Axis(info.x,cap.xmin,cap.xmax),Axis(info.y,cap.ymin,cap.ymax),Axis(info.z,cap.zmin,cap.zmax),Axis(info.r,cap.rmin,cap.rmax),Axis(info.u,cap.umin,cap.umax),Axis(info.v,cap.vmin,cap.vmax)},buttons=Enumerable.Range(0,(int)Math.Min(cap.buttons,32)).Select(n=>(info.buttons&(1u<<n))!=0).ToArray()});
        }return result.ToArray();
    }
}
public class ProtectionState {
    public double? permitted,target,distance;public string source="",system="CTCS",status="unavailable",reason="等待线路或车载限速";
    public double brake;public bool emergency;
}
public static class Protection {
    public static readonly string[] Systems={"AUTO","CTCS","TVM430","ETCS","LZB","PZB","KVB","AWS/TPWS"};
    public static double Curve(double limit,double target,double distance,Settings settings){
        if(target>=limit)return limit;double a=settings.Deceleration,t=settings.ReactionSeconds,v=target/3.6,d=Math.Max(0,distance-settings.SafetyDistance);
        return Math.Min(limit,Math.Max(v,-a*t+Math.Sqrt(a*a*t*t+v*v+2*a*d))*3.6);
    }
    public static ProtectionState Evaluate(Frame frame,Settings settings,string selected){
        var state=new ProtectionState{system=selected=="AUTO"?(frame.AvailableSystems.Length==1?frame.AvailableSystems[0]:"AUTO"):selected};
        if(!frame.Fresh||!frame.Speed.HasValue||frame.CabActivated!=true){state.reason=frame.CabActivationReason;return state;}
        double? limit=frame.Limit;
        if(frame.CabActive&&frame.CabPermitted.HasValue){limit=limit.HasValue?Math.Min(limit.Value,frame.CabPermitted.Value):frame.CabPermitted;state.source="游戏车载限速 + 线路与信号";}else state.source="线路与信号曲线（车载限速未提供）";
        if(!limit.HasValue)return state;state.permitted=limit;
        var targets=new List<Tuple<double,double,string>>();
        if(frame.Target.HasValue&&frame.Distance.HasValue)targets.Add(Tuple.Create(frame.Target.Value,frame.Distance.Value,"前方降速"));
        foreach(var point in frame.SpeedPoints){var v=JsonData.Number(JsonData.At(point,"speed"));var d=JsonData.Number(JsonData.At(point,"distance"));if(v.HasValue&&d.HasValue)targets.Add(Tuple.Create(v.Value,d.Value,"前方降速"));}
        foreach(var point in frame.Signals){var d=JsonData.Number(JsonData.At(point,"distance"));if(Convert.ToString(JsonData.At(point,"aspect"))=="Stop"&&d.HasValue)targets.Add(Tuple.Create(0.0,d.Value,"前方停车信号"));}
        state.reason="速度正常";
        foreach(var point in targets){double allowed=Curve(limit.Value,point.Item1,point.Item2,settings);if(allowed<state.permitted){state.permitted=allowed;state.target=point.Item1;state.distance=point.Item2;state.reason=point.Item3;}}
        double excess=frame.Speed.Value-state.permitted.Value;
        state.status=frame.Speed.Value==0?"normal":excess>=10?"EB":excess>=3?"SB":excess>0?"warning":"normal";
        // This is a configurable DMI training overlay; native TVM/PZB state is not fabricated.
        state.brake=state.status=="SB"?100:0;state.emergency=state.status=="EB";
        return state;
    }
}
public class MappingSession {
    public string lease="",vehicle="",cab="",route="",device="",message="未启用摇杆映射",lastEvent="";
    public bool armed,protection,emergencyLatched,serviceLatched;public long sequence;
    public DateTime inputAt;public double throttle,brake;public ProtectionState supervision=new ProtectionState();
    public readonly List<object> events=new List<object>();
    public void Event(string text){message=text;if(text==lastEvent)return;lastEvent=text;events.Insert(0,new{time=DateTime.Now.ToString("HH:mm:ss"),text=text});if(events.Count>80)events.RemoveAt(80);}
    public void Stop(string reason){armed=false;lease="";Event(reason);}
    public object Status(){return new{armed=armed,protection=protection,emergencyLatched=emergencyLatched,serviceLatched=serviceLatched,throttle=throttle,brake=brake,message=message,supervision=supervision,events=events.ToArray()};}
}
public class Operations {
    public string mode="FS",requestedMode="",vehicle="",route="",message="等待司机室激活",brakeTest="未测试";
    public bool enabled,warning,penalty;public double elapsed,distance;DateTimeOffset? previous;
    public readonly List<object> events=new List<object>();
    public void Event(string text){if(message==text)return;message=text;events.Insert(0,new{time=DateTime.Now.ToString("HH:mm:ss"),text=text});if(events.Count>40)events.RemoveAt(40);}
    public void ResetTimer(){elapsed=distance=0;warning=false;}
    public void Tick(Frame f,string configuredMode){
        if(!f.Fresh||f.CabActivated!=true){enabled=false;ResetTimer();previous=null;Event("司机室未激活，DMI 操作停用");return;}
        if(vehicle!=f.Vehicle+"|"+f.VehicleId||route!=f.RouteId){vehicle=f.Vehicle+"|"+f.VehicleId;route=f.RouteId;requestedMode="";penalty=false;ResetTimer();previous=null;brakeTest="未测试";Event("司机室激活验证通过");}
        string next=requestedMode.Length>0?requestedMode:configuredMode;if(next!=mode){mode=next;ResetTimer();previous=null;}
        enabled=mode=="OS"||(mode=="CO"&&!f.Limit.HasValue)||(mode=="CS"&&f.Signal=="HU");
        DateTimeOffset now;double dt=0;if(DateTimeOffset.TryParse(f.GameTime,out now)){if(previous.HasValue)dt=(now-previous.Value).TotalSeconds;previous=now;}
        if(dt<0||dt>15){ResetTimer();dt=0;Event("游戏时间发生跳变，警惕计时已重新开始");}
        if(!enabled||f.Speed<=.5){ResetTimer();return;}
        if(dt<=0)return;elapsed+=dt;distance+=f.Speed.GetValueOrDefault()/3.6*dt;
        double warnDistance=mode=="CS"?150:200,brakeDistance=mode=="CS"?200:300;
        if(elapsed>=60||distance>=brakeDistance){penalty=true;warning=true;Event("警惕确认超时，训练制动已锁存；停车后缓解");}
        else if(elapsed>=50||distance>=warnDistance){warning=true;Event(mode=="OS"?"目视确认：请按警惕键":mode=="CO"?"引导确认：请按警惕键":"机信确认：请按警惕键");}
    }
    public void Vigilance(){ResetTimer();Event(penalty?"警惕已确认，制动仍锁存；停车后按缓解":"警惕／待确认消息已确认");}
    public object Status(){return new{mode=mode,enabled=enabled,warning=warning,penalty=penalty,elapsed=Math.Round(elapsed,1),distance=Math.Round(distance,1),message=message,brakeTest=brakeTest,events=events.ToArray()};}
}
}

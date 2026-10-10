using System;using TSW7DMI;using CTCSWeb;
class OperationsTests {
    static int count;static void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
    static void Main(){
        var f=new Frame{Fresh=true,CabActivated=true,Speed=72,Vehicle="test",RouteId="route",Limit=120};var ops=new Operations();var origin=DateTimeOffset.Parse("2026-10-07T12:00:00+08:00");
        Action<double> tick=seconds=>{f.GameTime=origin.AddSeconds(seconds).ToString("o");ops.Tick(f,"OS");};
        tick(0);tick(9);Check(!ops.warning&&ops.distance==180,"vigilance uses actual elapsed game time and speed");tick(10);Check(ops.warning&&!ops.penalty,"200 m triggers OS confirmation warning");tick(10);Check(ops.elapsed==10,"paused game clock never advances vigilance");tick(15);Check(ops.penalty,"300 m triggers latched penalty");ops.Vigilance();Check(ops.penalty&&!ops.warning,"vigilance button does not silently release a penalty");
        f.CabActivated=false;tick(16);Check(!ops.enabled&&ops.elapsed==0,"deactivated cab disables vigilance timing");f.CabActivated=true;f.RouteId="new-route";tick(17);Check(!ops.penalty,"new route clears old-cab penalty state");
        ops=new Operations();f.Speed=3.6;tick(0);for(int i=10;i<=50;i+=10)tick(i);Check(ops.warning&&!ops.penalty,"50 s warns at low speed before 200 m");tick(60);Check(ops.penalty,"60 s penalty independent of distance");
        ops=new Operations();f.GameTime=origin.ToString("o");ops.Tick(f,"FS");f.GameTime=origin.AddSeconds(10).ToString("o");ops.Tick(f,"FS");Check(!ops.enabled&&!ops.warning,"FS does not invent periodic OS vigilance");
        Check(JsonData.BooleanResult(JsonData.Parse("{\"bActive\":true}"))==true,"actual TSW bActive field activates cab");Check(JsonData.BooleanResult(JsonData.Parse("{\"NoData\":true}"))==null,"NoData flag cannot activate cab");Check(JsonData.BooleanResult(JsonData.Parse("{\"ReturnValue\":1}"))==null,"numeric cab activation is ambiguous");
        Check(JsonData.BooleanResult(JsonData.Parse("{\"IsActive?\":true}"))==true,"Class 331 activation field is supported");
        Check(JsonData.BooleanResult(JsonData.At(JsonData.Parse("{\"Result\":\"Success\",\"Values\":{\"Result\":true}}"),"Values"))==true,"Class 710 boolean Result activates cab");
        Check(JsonData.BooleanResult(JsonData.At(JsonData.Parse("{\"Result\":\"Success\",\"Values\":{\"Result\":false}}"),"Values"))==false,"Class 710 boolean Result preserves inactive state");
        Check(JsonData.BooleanResult(JsonData.Parse("{\"Result\":\"Success\"}"))==null,"API success string is never cab activation");
        var axes=new AxisMapping();var joy=new JoystickDevice{axes=new[]{-1.0,0},buttons=new bool[3]};Check(axes.Read(joy).brake==100&&axes.Read(joy).throttle==0,"native joystick full brake wins over traction");axes.power.invert=true;Check(axes.Read(joy).throttle==100,"native axis inversion matches browser preview");axes.power.invert=false;joy.axes[0]=.02;Check(axes.Read(joy).throttle==0,"native neutral deadzone rejects jitter");axes.vigilanceButton=2;joy.buttons[2]=true;Check(axes.Read(joy).vigilance,"native vigilance button is independently mapped");
        var state=Protection.Evaluate(new Frame{Fresh=true,CabActivated=null,Speed=100,Limit=120},new Settings(),"TVM430");Check(!state.permitted.HasValue,"unknown cab activation cannot create active protection");
        var map=DriveMap.Builtin("RVM_BR_Class170_Test_C");Check(map.Valid()&&Math.Abs(map.Input(0,100,false)-.2)<.00001,"built-in Class 170 service detent remains exact");map.emergency=map.service;Check(!map.Valid(),"calibration cannot merge service and emergency detents");
        map=DriveMap.Builtin("RVM_GOB_Class710_DMS_A_C");Check(map.Valid()&&map.Input(0,100,false)==-.75,"Class 710 service uses automatic traction-brake profile");
        Check(map.Input(0,0,false)==0&&map.Input(100,0,false)==1&&map.Input(0,0,true)==-1,"Class 710 neutral power and emergency are distinct");
        Check(DriveMap.Builtin("Unknown_Class710_C")==null,"unidentified variants do not borrow Class 710 profile");
        var thresholdFrame=new Frame{Fresh=true,CabActivated=true,Speed=82.99,Limit=80};
        state=Protection.Evaluate(thresholdFrame,new Settings(),"CTCS");Check(state.status=="warning"&&state.brake==0&&!state.emergency,"below three km/h overspeed warns without brake intervention");
        thresholdFrame.Speed=0;thresholdFrame.Limit=-1;state=Protection.Evaluate(thresholdFrame,new Settings(),"CTCS");Check(state.status=="normal"&&state.brake==0&&!state.emergency,"standstill never generates overspeed intervention");thresholdFrame.Limit=80;thresholdFrame.Speed=83;state=Protection.Evaluate(thresholdFrame,new Settings(),"CTCS");Check(state.status=="SB"&&state.brake==100&&!state.emergency,"exactly three km/h overspeed applies full service brake");
        thresholdFrame.Speed=90;state=Protection.Evaluate(thresholdFrame,new Settings(),"CTCS");Check(state.emergency,"ten km/h overspeed still triggers emergency brake");
        Check(JsonData.BooleanResult(JsonData.Parse("{\"bReturnValue\":true}"))==true&&JsonData.BooleanResult(JsonData.Parse("{\"bReturnValue\":false}"))==false,"ICE bReturnValue preserves actual cab activation boolean");
        var reverser=JsonData.Parse("{\"namedValues\":[{\"valueSource\":\"Output\",\"valueRange\":{\"min\":0.5,\"max\":1.5},\"displayName\":\"前牵位\"},{\"valueSource\":\"Output\",\"valueRange\":{\"min\":-0.5,\"max\":0.5},\"displayName\":\"关闭\"}]}");
        Check(JsonData.ReverserForward(reverser,0,1)==true,"DB forward activation uses the labelled output range");
        Check(JsonData.ReverserForward(reverser,1,0)==false,"DB reverser off cannot activate the cab");
        Check(JsonData.ReverserForward(reverser,1,null)==null,"DB missing reverser output never falls back to an assumed input value");
        var m7=JsonData.Parse("{\"namedValues\":[{\"valueSource\":\"Output\",\"valueRange\":{\"min\":-0.5,\"max\":0.5},\"displayName\":\"空挡\"},{\"valueSource\":\"Output\",\"valueRange\":{\"min\":1.5,\"max\":2.5},\"displayName\":\"关闭\"}]}");
        Check(JsonData.ReverserForward(m7,.333,0,true)==true,"M7A neutral is an inserted reverser position");
        Check(JsonData.ReverserForward(m7,1,2,true)==false,"M7A off output two is inactive despite positive value");
        Check(JsonData.ReverserForward(m7,1,null,true)==null,"M7A missing output cannot activate from input alone");
        var platformFrame=new Frame();
        JsonData.Track(platformFrame,JsonData.Parse("{\"stations\":[{\"stationName\":\"A\",\"distanceToStationCM\":10000,\"platformLength\":20000,\"platformSide\":\"Left\"}]}"));
        Check(Object.Equals(JsonData.At(platformFrame.PlatformInfo,"inRange"),false),"upcoming platform side is received but not yet in valid range");
        JsonData.Track(platformFrame,JsonData.Parse("{\"stations\":[{\"stationName\":\"A\",\"distanceToStationCM\":-10000,\"platformLength\":20000,\"platformSide\":\"Right\"}]}"));
        Check(Object.Equals(JsonData.At(platformFrame.PlatformInfo,"inRange"),true),"platform side remains valid after entry within its length");
        JsonData.Track(platformFrame,JsonData.Parse("{\"stations\":[]}"));
        Check(platformFrame.PlatformInfo==null,"missing platform metadata clears stale side");
        Console.WriteLine(count+" operation tests passed.");
    }
}

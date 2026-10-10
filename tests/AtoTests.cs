using System;using CTCSWeb;using TSW7DMI;
class AtoTests {
 static int count;static readonly DateTimeOffset origin=DateTimeOffset.Parse("2026-10-07T12:00:00Z");
 static void Check(bool v,string name){if(!v)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
 static Frame Frame(double distance=800,double speed=0){var f=new Frame{Fresh=true,CabActivated=true,BrakeSupported=true,GameAdvancing=true,Speed=speed,Vehicle="test",VehicleId="cab1",RouteId="route",Station="Next",Platform="1",StationDistance=distance,GameTime=origin.ToString("o"),Timestamp=origin.UtcDateTime};Station(f,distance);return f;}
 static void Station(Frame f,double d){f.StationDistance=d;f.Stations=new object[]{new System.Collections.Generic.Dictionary<string,object>{{"name",f.Station},{"platform",f.Platform},{"distance",d}}};}
 static void Advance(Frame f,double seconds){f.GameTime=origin.AddSeconds(seconds).ToString("o");f.Timestamp=origin.AddSeconds(seconds).UtcDateTime;}
 static bool Fails(Action a){try{a();return false;}catch(InvalidOperationException){return true;}catch(ArgumentException){return true;}}
 static void Main(){
  var f=Frame();var a=new AtoSession();var p=new ProtectionState{permitted=100};a.Start(f,0,80,.65);Advance(f,.5);a.Step(f,p,false);Check(a.throttle>0&&a.brake==0,"ATO accelerates from real stopped frame toward its locked station");Check(a.targetSpeed<=80,"ATO speed ceiling is enforced");Check(!a.Step(f,p,false),"same telemetry frame never advances control twice");
  Advance(f,1);p.permitted=0;a.Step(f,p,false);Check(a.throttle==0&&a.brake==100,"red signal zero permission holds full service brake");
  Advance(f,1.5);p.permitted=100;a.Step(f,p,false);Check(a.throttle>0,"cleared signal allows explicit active ATO to continue");
  Advance(f,2);a.Step(f,p,true);Check(a.emergency&&a.brake==100&&a.throttle==0,"vigilance penalty overrides automatic traction");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.65);f.VehicleId="cab2";Advance(f,.5);Check(Fails(()=>a.Step(f,p,false)),"same-class cab change invalidates ATO");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.65);f.GameAdvancing=false;Check(Fails(()=>a.Step(f,p,false)),"paused game disarms ATO");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.65);Advance(f,20);Check(Fails(()=>a.Step(f,p,false)),"clock jumps cannot advance ATO blindly");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.65);Station(f,1600);Advance(f,.5);Check(Fails(()=>a.Step(f,p,false)),"station distance jump fails closed");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.65);f.Station="Other";Station(f,1200);Advance(f,1);a.Step(f,p,false);Check(a.station=="Next"&&a.remaining==800,"new next station never replaces locked stop during brief data gap");Advance(f,2);a.Step(f,p,false);Advance(f,3);a.Step(f,p,false);Advance(f,4);a.Step(f,p,false);Check(!a.hasDestination&&a.active,"missing target falls back to speed-limit running after data grace period");
  f=Frame(20);a=new AtoSession();a.Start(f,0,80,.65);Station(f,2);Advance(f,1);f.Speed=.1; // bounded final position change
  Station(f,8);a.Step(f,p,false);Station(f,.5);Advance(f,2);a.Step(f,p,false);Check(a.arrived&&a.brake==100&&a.throttle==0,"stopped at target holds service brake");a.Stop(a.message);Check(!a.active&&a.arrived,"arrival does not arm another departure");
  f=Frame();f.BrakeSupported=false;Check(Fails(()=>new AtoSession().Start(f,0,80,.65)),"uncalibrated vehicle cannot start ATO");f=Frame();Check(Fails(()=>new AtoSession().Start(f,Double.NaN,80,.65)),"nonfinite offset rejected");Check(Fails(()=>new AtoSession().Start(f,0,400,.65)),"invalid speed ceiling rejected");
  foreach(double actualBrake in new[]{.45,.65,.85}){
   f=Frame(800);a=new AtoSession();a.Start(f,0,80,.4);double distance=800,speed=0;bool passed=false;
   for(int i=1;i<=2400;i++){Advance(f,i*.5);f.Speed=speed*3.6;Station(f,Math.Max(0,distance));a.Step(f,p,false);double acceleration=a.throttle/100*.7-a.brake/100*actualBrake-.008;double next=Math.Max(0,speed+acceleration*.5);distance-=(speed+next)*.25;speed=next;if(a.arrived){passed=true;break;}if(a.emergency)break;}
   Check(passed&&distance>=-3&&distance<=3,"closed-loop stop within tolerance with service deceleration "+actualBrake);
  }
  f=Frame();a=new AtoSession();a.Start(f,0,80,.5);f.Timestamp=f.Timestamp.AddMilliseconds(500);Check(!a.Step(f,p,false),"repeated game clock sample never integrates wall time");
  f=Frame();a=new AtoSession();a.Start(f,0,80,.5);bool stallRejected=false;for(int i=1;i<=45;i++){Advance(f,i*.5);if(Fails(()=>a.Step(f,p,false))){stallRejected=true;break;}}Check(stallRejected,"sustained traction without motion exits for driver inspection");
  f=Frame(100);a=new AtoSession();a.Start(f,80,50,.35);double left=180,velocity=0;bool stopped=false;var lag=new System.Collections.Generic.Queue<double[]>();for(int j=0;j<4;j++)lag.Enqueue(new double[]{0,0});
  for(int i=1;i<=1800;i++){Advance(f,i*.5);f.Speed=velocity*3.6;if(left>=80)Station(f,left-80);else f.Stations=new object[0];a.Step(f,p,false);lag.Enqueue(new[]{a.throttle,a.brake});var applied=lag.Dequeue();double next=Math.Max(0,velocity+(applied[0]/100*.7-applied[1]/100*.55-.008)*.5);left-=(velocity+next)*.25;velocity=next;if(a.arrived){stopped=true;break;}if(a.emergency)break;}
  Check(stopped&&left>=-3&&left<=3,"positive stop offset survives passed marker and two-second actuator lag");
  var d=XInputDevices.ConvertPad(0,new XInputDevices.Pad{lx=short.MinValue,rx=short.MaxValue,leftTrigger=0,rightTrigger=255,buttons=4097});Check(d.axes[0]==-1&&d.axes[2]==1&&d.axes[4]==-1&&d.axes[5]==1,"XInput axes and triggers use calibrated signed range");Check(d.buttons[0]&&d.buttons[10],"XInput D-pad and A are distinct buttons");
  var m=new AxisMapping{mode="buttons",powerButton=0,brakeButton=1};var dev=new JoystickDevice{axes=new double[0],buttons=new[]{true,false}};Check(m.Read(dev).throttle==35,"button-only device can drive without axes");dev.buttons[1]=true;Check(m.Read(dev).throttle==0&&m.Read(dev).brake==100,"button brake overrides button traction");
  a=new AtoSession();a.Request(0,80,.65);Check(a.waiting&&!a.active&&a.throttle==0,"activation without telemetry waits without producing traction");a.Stop("cancel");Check(!a.waiting&&!a.active,"stop cancels pending activation");
  f=Frame(800,72);a=new AtoSession();a.ConfigurePid(.1,0,0,1.5,.5);a.Start(f,0,80,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=80},false);double low=a.throttle;
  a.ConfigurePid(.2,0,0,1.5,.5);Advance(f,1);a.Step(f,new ProtectionState{permitted=80},false);Check(a.throttle>low&&low>0,"live proportional gain changes actual traction demand");
  a.ConfigurePid(0,0,1,1.5,.5);f.Speed=75;Advance(f,1.5);a.Step(f,new ProtectionState{permitted=80},false);Check(a.dTerm<0&&!Double.IsNaN(a.dTerm),"filtered derivative reacts to measured acceleration");
  Check(Fails(()=>a.ConfigurePid(Double.NaN,0,0,1,.5))&&a.kd==1,"invalid PID is rejected atomically");
  a.ConfigurePid(5,2,0,.1,.5);f=Frame();a.Start(f,0,80,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=100},false);Check(a.iTerm==0,"saturated traction does not wind up the integrator");
  a.ConfigurePid(.1,.1,.1,.1,.5);Check(a.iTerm==0&&a.dTerm==0,"live tuning clears stored integral and derivative terms");
  f=Frame(2000,77.7);f.Limit=80;a=new AtoSession();a.Start(f,0,80,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=80},false);Check(a.phase=="coast"&&a.throttle==0&&a.brake==0,"near target ATO commands neutral coasting");
  f.Speed=77;Advance(f,1);a.Step(f,new ProtectionState{permitted=80},false);Check(a.phase=="coast","coasting hysteresis prevents rapid traction cycling");
  f.Speed=75;Advance(f,1.5);a.Step(f,new ProtectionState{permitted=80},false);Check(a.throttle>0,"below coasting band traction resumes");
  f.Speed=79;Advance(f,2);a.Step(f,new ProtectionState{permitted=80},false);Check(a.brake>0&&a.phase=="brake","overspeed braking overrides coasting");
  f.Speed=77.7;a.allowCoast=false;Advance(f,2.5);a.Step(f,new ProtectionState{permitted=80},false);Check(a.throttle>0,"coasting can be disabled while running");
  a.UpdateCurve(f,new Settings());var state=JsonData.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(a.Status()));var curve=(object[])JsonData.At(state,"curve");Check(curve.Length>2&&Math.Abs(JsonData.Number(JsonData.At(curve[0],"speed")).Value-a.targetSpeed)<1e-8,"ATO curve starts at the exact controller target speed");
  Check(JsonData.Number(JsonData.At(curve[curve.Length-1],"speed"))==0,"ATO station target curve ends at zero speed");
  f.Signals=new object[]{new System.Collections.Generic.Dictionary<string,object>{{"distance",400.0},{"aspect","Stop"}}};a.UpdateCurve(f,new Settings());state=JsonData.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(a.Status()));curve=(object[])JsonData.At(state,"curve");Check(JsonData.Number(JsonData.At(curve[curve.Length-1],"distance"))==400&&JsonData.Number(JsonData.At(curve[curve.Length-1],"speed"))==0,"red signal truncates ATO curve before the station");
  a.Stop("stop");Check(a.curve.Length==0,"ATO exit removes its stale target curve");
  f=Frame();f.Station="";f.StationDistance=null;f.Stations=new object[0];f.Limit=80;a=new AtoSession();a.Start(f,0,100,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=80},false);Check(a.active&&!a.hasDestination&&a.throttle>0&&a.targetSpeed==78,"no destination starts speed-limit cruise with existing speed margin");
  a.UpdateCurve(f,new Settings());state=JsonData.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(a.Status()));curve=(object[])JsonData.At(state,"curve");Check(JsonData.At(state,"remaining")==null&&JsonData.Number(JsonData.At(curve[curve.Length-1],"speed"))==78,"cruise exposes unknown distance and has no invented station stop");
  f.Speed=77.7;Advance(f,1);a.Step(f,new ProtectionState{permitted=80},false);Check(a.phase=="coast"&&a.throttle==0,"speed-limit cruise retains automatic coasting");
  Advance(f,1.5);a.Step(f,new ProtectionState{permitted=0},false);Check(a.targetSpeed==0&&a.brake==100&&!a.arrived,"cruise respects stop authority without inventing station arrival");
  f.Signals=new object[]{new System.Collections.Generic.Dictionary<string,object>{{"distance",300.0},{"aspect","Stop"}}};a.UpdateCurve(f,new Settings());state=JsonData.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(a.Status()));curve=(object[])JsonData.At(state,"curve");Check(JsonData.Number(JsonData.At(curve[curve.Length-1],"distance"))==300&&JsonData.Number(JsonData.At(curve[curve.Length-1],"speed"))==0,"cruise target curve still ends at a red signal");
  f.Station="Restored";Station(f,200);f.Signals=new object[0];Advance(f,2);a.Step(f,new ProtectionState{permitted=80},false);Check(a.hasDestination&&a.station=="Restored"&&a.remaining==200&&a.targetSpeed<78,"restored destination switches cruise to station approach");
  Advance(f,2.5);Check(Fails(()=>a.Step(f,new ProtectionState(),false)),"no destination never bypasses missing speed authority");
  f=Frame(2000);a=new AtoSession();a.Start(f,0,120,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=120},false);Check(a.throttle==100&&a.brake==0,"ATO may command full traction instead of the former 70 percent ceiling");
  f=Frame(2);a=new AtoSession();a.Start(f,0,80,.65);Advance(f,.5);a.Step(f,p,false);Check(!a.arrived&&a.throttle>0&&a.brake==0&&a.targetSpeed<=1.5&&AtoSession.TargetAt(15,80,80,.65)<=1.5,"stopping two metres short requests correction instead of declaring arrival");
  Advance(f,1);a.Step(f,new ProtectionState{permitted=0},false);Check(!a.arrived&&a.throttle==0&&a.brake==100,"short-stop correction never overrides a red signal");
  f=Frame(12,10);a=new AtoSession();a.Start(f,0,80,.65);for(int i=1;i<=6;i++){Advance(f,i*.5);a.Step(f,p,false);}f.Speed=0;Station(f,1);Advance(f,3.5);a.Step(f,p,false);Check(!a.arrived&&a.throttle>0&&a.iTerm>=0,"final approach clears negative braking integral before correcting a short stop");
  foreach(double shortDistance in new[]{1.0,2.5,10.0}){
   f=Frame(shortDistance);a=new AtoSession();a.Start(f,0,80,.65);double distance=shortDistance,speed=0;bool reached=false;
   for(int i=1;i<=600;i++){Advance(f,i*.25);f.Speed=speed*3.6;Station(f,Math.Max(0,distance));a.Step(f,p,false);double next=Math.Max(0,speed+(a.throttle/100*.7-a.brake/100*.85-.015)*.25);distance-=(speed+next)*.125;speed=next;if(a.arrived){reached=true;break;}if(a.emergency)break;}
   Check(reached&&distance>=-.75&&distance<=.75,"short-stop closed-loop correction reaches marker tolerance from "+shortDistance+" m");
  }
  f=Frame(14);f.PlatformLength=211.5;a=new AtoSession();a.Start(f,0,80,.65);Check(Math.Abs(a.remaining-215.5)<.001&&a.platformAdvance==201.5,"platform entry is advanced to ten metres before platform end");
  f=Frame(1,7.2);f.PlatformLength=100;a=new AtoSession();a.Start(f,0,80,.65);Advance(f,1);Station(f,60);a.Step(f,p,false);Check(a.station=="Next"&&Math.Abs(a.remaining-89)<.001,"passing platform entry ignores later markers of the same station");
  f.Station="Other";Station(f,4000);for(int i=2;i<=5;i++){Advance(f,i);a.Step(f,p,false);}Check(a.hasDestination&&a.station=="Next"&&a.remaining<89,"next station telemetry cannot replace locked in-platform stop");
  f=Frame(14);f.PlatformLength=211.5;a=new AtoSession{platformEndMargin=30};a.Start(f,0,80,.65);Check(Math.Abs(a.remaining-195.5)<.001,"custom platform end margin moves stopping target earlier");
  a=new AtoSession{platformEndMargin=0};a.Start(f,0,80,.65);Check(Math.Abs(a.remaining-225.5)<.001,"zero platform end margin targets platform end");
  f=Frame(100);a=new AtoSession();a.Start(f,0,80,.65);a.SkipStation(f);Advance(f,1);a.Step(f,p,false);Check(!a.hasDestination&&a.active,"skipped station cannot be reacquired from unchanged telemetry");
  f.Station="Later";Station(f,1000);Advance(f,2);a.Step(f,p,false);Check(a.hasDestination&&a.station=="Later","skip cruise acquires a later station when it becomes available");
  a.SkipStation(f);Advance(f,3);a.Step(f,new ProtectionState{permitted=0},false);Check(a.brake==100&&a.throttle==0,"skipping a station never bypasses red signal authority");
  a.Stop("stop");Check(Fails(()=>a.SkipStation(f)),"skip cannot start inactive ATO");
  f=Frame(2000,90);a=new AtoSession();a.Start(f,0,120,.65);Advance(f,.5);a.Step(f,new ProtectionState{permitted=80,emergency=true,brake=100},false);Check(a.active&&!a.emergency&&a.phase=="brake"&&a.throttle==0,"ATO controls overspeed itself without ATP penalty exit");
  Console.WriteLine(count+" ATO/device tests passed.");
 }
}

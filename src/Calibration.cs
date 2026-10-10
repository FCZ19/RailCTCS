using System;using System.Collections.Generic;using System.Linq;
namespace TSW7DMI {
public class DriveMap {
    public bool automatic,split;public double powerStep;public string throttleControl="";
    public static DriveMap Discover(string vehicle,string control,double min,double max,double low,double high,object display){
        var names=JsonData.At(display,"namedValues") as object[];if(names==null||!(min<max&&low<0&&high>0))return null;
        bool neutral=false,emergency=false,power=false;double? service=null;double step=0;
        foreach(var n in names){string label=Convert.ToString(JsonData.At(n,"displayName")).ToLowerInvariant();if(Convert.ToString(JsonData.At(n,"valueSource"))!="Output")continue;var a=JsonData.Number(JsonData.At(n,"valueRange.min"));var b=JsonData.Number(JsonData.At(n,"valueRange.max"));if(!a.HasValue||!b.HasValue)continue;
            if((label.Contains("关闭")||label.Contains("空挡")||label.Contains("空档")||label.Contains("off")||label.Contains("neutral"))&&a<=0&&b>=0)neutral=true;
            if((label.Contains("紧急")||label.Contains("emergency"))&&a<=low&&b>=low)emergency=true;
            if(label.Contains("最大制动")||label.Contains("full service")||label.Contains("maximum brake")){double mid=(a.Value+b.Value)/2,integer=Math.Round(mid);service=integer>=a&&integer<=b?integer:mid;}
            if((label.Contains("power")||label.Contains("牵引")||label.Contains("动力"))&&a<=high&&b>=high){power=true;if(label.Contains("powernumber"))step=1;}
        }
        if(!neutral||!emergency||!power||!service.HasValue||service<=low||service>=0)return null;
        Func<double,double> input=v=>min+(v-low)/(high-low)*(max-min);
        var map=new DriveMap{vehicle=vehicle,control=control,minimum=min,maximum=max,neutral=input(0),power=max,service=input(service.Value),emergency=min,neutralOutput=0,powerOutput=high,serviceOutput=service.Value,emergencyOutput=low,automatic=true,powerStep=step};return map.Valid()?map:null;
    }
    public string vehicle,control;public double minimum,maximum,neutral,power,service,emergency,neutralOutput,powerOutput,serviceOutput,emergencyOutput;
    public static DriveMap Builtin(string vehicle){
        if(vehicle.StartsWith("RVM_BR_Class170_",StringComparison.Ordinal))return new DriveMap{vehicle=vehicle,control="CurrentDrivableActor/IrregularLever_ThrottleBrake",minimum=0,maximum=1,neutral=.5,power=1,service=.2,emergency=0,neutralOutput=0,powerOutput=7,serviceOutput=-3,emergencyOutput=-4};
        if(vehicle.StartsWith("RVM_GOB_Class710_",StringComparison.Ordinal))return new DriveMap{vehicle=vehicle,control="CurrentDrivableActor/TractionBrakeControl",minimum=-1,maximum=1,neutral=0,power=1,service=-.75,emergency=-1,neutralOutput=0,powerOutput=1,serviceOutput=-3,emergencyOutput=-4};
        return null;
    }
    public bool Valid(){if(split)return !String.IsNullOrWhiteSpace(vehicle)&&control=="CurrentDrivableActor/TrainBrake"&&throttleControl=="CurrentDrivableActor/Throttle"&&minimum<=0&&minimum>=-.2&&maximum==1&&neutralOutput==0&&serviceOutput==9&&emergencyOutput==10;double[] numbers={minimum,maximum,neutral,power,service,emergency,neutralOutput,powerOutput,serviceOutput,emergencyOutput};return !String.IsNullOrWhiteSpace(vehicle)&&control!=null&&System.Text.RegularExpressions.Regex.IsMatch(control,@"^CurrentDrivableActor/[A-Za-z0-9_() -]{1,100}$")&&numbers.All(n=>!Double.IsNaN(n)&&!Double.IsInfinity(n))&&maximum>minimum&&new[]{neutral,power,service,emergency}.All(n=>n>=minimum&&n<=maximum)&&(power-neutral)*(service-neutral)<0&&(emergency-service)*(service-neutral)>0&&(powerOutput-neutralOutput)*(serviceOutput-neutralOutput)<0&&(emergencyOutput-serviceOutput)*(serviceOutput-neutralOutput)>0;}
    public double Input(double throttle,double brake,bool eb){return eb?emergency:brake>0?neutral+(service-neutral)*brake/100:neutral+(power-neutral)*throttle/100;}
    public bool IsEmergency(double? output){return output.HasValue&&Math.Abs(output.Value-emergencyOutput)<.02;}
    public bool IsBraking(double? output){return output.HasValue&&(output.Value-neutralOutput)*(serviceOutput-neutralOutput)>0.01;}
}
public class LeverCapture {public double input,output;}
public class CalibrationDraft {public string vehicle,control;public Dictionary<string,LeverCapture> points=new Dictionary<string,LeverCapture>();}
}

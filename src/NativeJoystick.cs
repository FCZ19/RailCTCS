using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Threading;
namespace CTCSWeb {
// Use the user's already-installed RailBridge SDL runtime. No driver is installed.
public static class NativeJoystick {
    static volatile JoystickDevice[] snapshot=new JoystickDevice[0];static int started;static string error="";
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibrary(string path);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_InitSubSystem(uint flags);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_SetHint(string name,string value);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_NumJoysticks();
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr SDL_JoystickOpen(int index);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern void SDL_JoystickClose(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickGetAttached(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickInstanceID(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickGetDeviceInstanceID(int index);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr SDL_JoystickName(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickNumAxes(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickNumButtons(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SDL_JoystickNumHats(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern byte SDL_JoystickGetHat(IntPtr joystick,int hat);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern ushort SDL_JoystickGetVendor(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern ushort SDL_JoystickGetProduct(IntPtr joystick);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern short SDL_JoystickGetAxis(IntPtr joystick,int axis);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern byte SDL_JoystickGetButton(IntPtr joystick,int button);
    [DllImport("SDL2.dll",CallingConvention=CallingConvention.Cdecl)]static extern void SDL_JoystickUpdate();
    public static string Error {get{return error;}}
    public static void Start(){if(Interlocked.Exchange(ref started,1)!=0)return;new Thread(Run){IsBackground=true,Name="DMI joystick reader"}.Start();}
    public static JoystickDevice[] Read(){return snapshot.Concat(XInputDevices.Read()).Concat(Joysticks.Read()).ToArray();}
    static void Run(){var handles=new Dictionary<int,IntPtr>();try{
        string library=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"RailBridge","SDL2.dll");
        if(!File.Exists(library)||LoadLibrary(library)==IntPtr.Zero){error="原生 SDL 摇杆运行库不可用，可使用浏览器手柄";return;}
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS","1");SDL_SetHint("SDL_JOYSTICK_HIDAPI","1");SDL_SetHint("SDL_JOYSTICK_RAWINPUT","1");if(SDL_InitSubSystem(0x200)!=0){error="SDL 摇杆初始化失败";return;}
        while(true){
            SDL_JoystickUpdate();
            foreach(var pair in handles.ToArray())if(SDL_JoystickGetAttached(pair.Value)==0){SDL_JoystickClose(pair.Value);handles.Remove(pair.Key);}
            for(int i=0;i<Math.Min(64,SDL_NumJoysticks());i++){int instance=SDL_JoystickGetDeviceInstanceID(i);if(instance<0||handles.ContainsKey(instance))continue;IntPtr handle=SDL_JoystickOpen(i);if(handle!=IntPtr.Zero)handles[SDL_JoystickInstanceID(handle)]=handle;}
            var devices=new List<JoystickDevice>();foreach(var pair in handles){IntPtr h=pair.Value;int axes=Math.Max(0,Math.Min(64,SDL_JoystickNumAxes(h))),buttons=Math.Max(0,Math.Min(256,SDL_JoystickNumButtons(h))),hats=Math.Max(0,Math.Min(16,SDL_JoystickNumHats(h)));if(axes+buttons+hats<1)continue;string name=Marshal.PtrToStringAnsi(SDL_JoystickName(h))??"Joystick";var values=Enumerable.Range(0,buttons).Select(b=>SDL_JoystickGetButton(h,b)!=0).ToList();var labels=Enumerable.Range(0,buttons).Select(b=>"按钮 "+(b+1)).ToList();for(int hat=0;hat<hats;hat++){int bits=SDL_JoystickGetHat(h,hat);for(int direction=0;direction<4;direction++){values.Add((bits&(1<<direction))!=0);labels.Add("方向帽 "+(hat+1)+" "+new[]{"上","右","下","左"}[direction]);}}devices.Add(new JoystickDevice{id="sdl:"+pair.Key+":"+name,name=name,backend="SDL / HID / DirectInput",profileKey="sdl:"+SDL_JoystickGetVendor(h)+":"+SDL_JoystickGetProduct(h)+":"+name,axes=Enumerable.Range(0,axes).Select(a=>{short v=SDL_JoystickGetAxis(h,a);return v<0?v/32768.0:v/32767.0;}).ToArray(),available=Enumerable.Repeat(true,axes).ToArray(),buttons=values.ToArray(),buttonNames=labels.ToArray()});}
            snapshot=devices.ToArray();Thread.Sleep(50);
        }
    }catch(Exception e){error="原生摇杆读取停止："+e.GetType().Name;snapshot=new JoystickDevice[0];}finally{foreach(var h in handles.Values)try{SDL_JoystickClose(h);}catch{}}}
}
public class AxisCalibration {public double min=-1,center=0,max=1,deadzone=.05;public bool invert;}
public class AxisMapping {
    public string mode="combined";public int powerAxis,brakeAxis=1,powerButton=-1,brakeButton=-1,emergencyButton=-1,ackButton=-1,vigilanceButton=-1;public AxisCalibration power=new AxisCalibration(),brake=new AxisCalibration();
    static double Value(double raw,AxisCalibration c,bool combined){
        if(c==null||new[]{raw,c.min,c.center,c.max,c.deadzone}.Any(v=>Double.IsNaN(v)||Double.IsInfinity(v))||c.max-c.min<.1||c.deadzone<0||c.deadzone>.3||Math.Abs(raw)>1.01||(combined&&(c.center<=c.min+.02||c.center>=c.max-.02)))throw new ArgumentException("轴校准无效");
        double value=combined?(raw>=c.center?(raw-c.center)/(c.max-c.center):(raw-c.center)/(c.center-c.min)):(raw-c.min)/(c.max-c.min);value=Math.Max(combined?-1:0,Math.Min(1,value));if(c.invert)value=combined?-value:1-value;return combined?Math.Sign(value)*Math.Max(0,(Math.Abs(value)-c.deadzone)/(1-c.deadzone)):Math.Max(0,(value-c.deadzone)/(1-c.deadzone));
    }
    double Axis(JoystickDevice d,int index,AxisCalibration c,bool combined){if(index<0||index>=d.axes.Length||(d.available!=null&&!d.available[index]))throw new ArgumentException("所选摇杆轴不可用");return Value(d.axes[index],c,combined);}
    bool Button(JoystickDevice d,int index){if(index==-1)return false;if(index<0||index>=d.buttons.Length)throw new ArgumentException("所选摇杆按钮不可用");return d.buttons[index];}
    public JoystickInput Read(JoystickDevice d){if(d==null)throw new InvalidOperationException("摇杆已断开");if(mode!="combined"&&mode!="separate"&&mode!="buttons")throw new ArgumentException("操纵方式无效");if(mode=="separate"&&powerAxis==brakeAxis)throw new ArgumentException("牵引和制动轴不能相同");var assigned=new[]{emergencyButton,ackButton,vigilanceButton,mode=="buttons"?powerButton:-1,mode=="buttons"?brakeButton:-1}.Where(b=>b>=0).ToArray();if(assigned.Distinct().Count()!=assigned.Length)throw new ArgumentException("功能按钮不能重复映射");if(mode=="buttons"&&(powerButton<0||brakeButton<0))throw new ArgumentException("请选择牵引和制动按钮");double v=mode=="buttons"?0:Axis(d,powerAxis,power,mode=="combined");var input=new JoystickInput{throttle=mode=="buttons"?(Button(d,powerButton)?35:0):Math.Max(0,v)*100,brake=mode=="buttons"?(Button(d,brakeButton)?100:0):mode=="combined"?Math.Max(0,-v)*100:Axis(d,brakeAxis,brake,false)*100,emergency=Button(d,emergencyButton),ack=Button(d,ackButton),vigilance=Button(d,vigilanceButton)};if(input.brake>0)input.throttle=0;return input;}
}
public class JoystickInput {public double throttle,brake;public bool emergency,ack,vigilance;}
}

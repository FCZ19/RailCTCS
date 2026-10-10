using System;using System.Collections.Generic;using System.Linq;using System.Runtime.InteropServices;
namespace CTCSWeb {
public static class XInputDevices {
    [StructLayout(LayoutKind.Sequential)]public struct Pad {public ushort buttons;public byte leftTrigger,rightTrigger;public short lx,ly,rx,ry;}
    [StructLayout(LayoutKind.Sequential)]struct State {public uint packet;public Pad pad;}
    [DllImport("xinput1_4.dll",EntryPoint="XInputGetState")]static extern uint GetState(uint index,out State state);
    [DllImport("xinput9_1_0.dll",EntryPoint="XInputGetState")]static extern uint GetLegacyState(uint index,out State state);
    public static double Normalize(short v){return v<0?v/32768.0:v/32767.0;}
    public static JoystickDevice ConvertPad(uint index,Pad p){int[] masks={1,2,4,8,16,32,64,128,256,512,4096,8192,16384,32768};return new JoystickDevice{id="xinput:"+index,name="Xbox / XInput "+(index+1),backend="XInput",profileKey="xinput:"+index,axes=new[]{Normalize(p.lx),Normalize(p.ly),Normalize(p.rx),Normalize(p.ry),p.leftTrigger/127.5-1,p.rightTrigger/127.5-1},available=Enumerable.Repeat(true,6).ToArray(),axisNames=new[]{"左摇杆 X","左摇杆 Y","右摇杆 X","右摇杆 Y","左扳机","右扳机"},buttons=masks.Select(m=>(p.buttons&m)!=0).ToArray(),buttonNames=new[]{"方向上","方向下","方向左","方向右","Start","Back","左摇杆按下","右摇杆按下","LB","RB","A","B","X","Y"}};}
    public static JoystickDevice[] Read(){var result=new List<JoystickDevice>();try{for(uint i=0;i<4;i++){State state;uint code;try{code=GetState(i,out state);}catch(DllNotFoundException){code=GetLegacyState(i,out state);}catch(EntryPointNotFoundException){code=GetLegacyState(i,out state);}if(code==0)result.Add(ConvertPad(i,state.pad));}}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}return result.ToArray();}
}
}

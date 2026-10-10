using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
class DmiOverlay : Form {
 Button pinButton,minButton,maxButton,closeButton,systemButton;
 readonly WebView2 web=new WebView2();readonly Label status=new Label();readonly string root;readonly bool verify;
 public DmiOverlay(bool check){verify=check;root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
  Text="DMI 悬浮窗";TopMost=true;FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(30,50,751,620);MinimumSize=new Size(381,329);BackColor=Color.FromArgb(37,41,43);
  var caption=new Panel{Dock=DockStyle.Top,Height=28,BackColor=Color.FromArgb(37,41,43)};
  var title=new Label{Text="DMI",AutoSize=false,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.LightSteelBlue,Padding=new Padding(10,0,0,0)};caption.Controls.Add(title);
  closeButton=CaptionButton("×",()=>Close());maxButton=CaptionButton("□",()=>{MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;});minButton=CaptionButton("—",()=>WindowState=FormWindowState.Minimized);pinButton=CaptionButton("置顶 ●",TogglePin);pinButton.Width=64;
  systemButton=CaptionButton("C2/C3",ShowMenu);systemButton.Width=64;
  foreach(var button in new[]{systemButton,pinButton,minButton,maxButton,closeButton}){button.Dock=DockStyle.Right;caption.Controls.Add(button);}
  title.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)BeginMove(2);};caption.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left)BeginMove(2);};title.DoubleClick+=(s,e)=>maxButton.PerformClick();title.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowMenu();};caption.MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)ShowMenu();};
  web.Dock=DockStyle.Fill;Controls.Add(web);Controls.Add(caption);
  AddResizeGrip(DockStyle.Left,10);AddResizeGrip(DockStyle.Right,11);AddResizeGrip(DockStyle.Bottom,15);AddResizeGrip(DockStyle.Top,12);
  Resize+=(s,e)=>{if(maxButton!=null)maxButton.Text=WindowState==FormWindowState.Maximized?"❐":"□";};Shown+=async(s,e)=>await Boot();
 }
 Button CaptionButton(string text,Action action){var b=new Button{Text=text,Width=34,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(52,56,58),ForeColor=Color.White,TabStop=false};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>action();return b;}
 void TogglePin(){TopMost=!TopMost;pinButton.Text=TopMost?"置顶 ●":"置顶 ○";}
 void BeginMove(int hit){if(WindowState==FormWindowState.Maximized&&hit!=2)return;ReleaseCapture();var p=Cursor.Position;SendMessage(Handle,0xA1,(IntPtr)hit,(IntPtr)((p.Y<<16)|(p.X&65535)));}
 void AddResizeGrip(DockStyle dock,int hit){var grip=new Panel{Dock=dock,Width=5,Height=5,BackColor=Color.FromArgb(52,57,59),Cursor=(hit==10||hit==11)?Cursors.SizeWE:Cursors.SizeNS};grip.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){var p=PointToClient(Cursor.Position);int edge=hit;if(p.Y<12)edge=p.X<12?13:p.X>ClientSize.Width-12?14:12;else if(p.Y>ClientSize.Height-12)edge=p.X<12?16:p.X>ClientSize.Width-12?17:15;BeginMove(edge);}};Controls.Add(grip);grip.BringToFront();}
 [DllImport("user32.dll")]static extern bool ReleaseCapture();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
 [StructLayout(LayoutKind.Sequential)]struct BoundsRect{public int Left,Top,Right,Bottom;}
 protected override void WndProc(ref Message m){if(m.Msg==0x214){var r=(BoundsRect)Marshal.PtrToStructure(m.LParam,typeof(BoundsRect));int width=r.Right-r.Left,height=r.Bottom-r.Top,edge=m.WParam.ToInt32();if(edge==3||edge==6){width=(int)Math.Round((height-38)*741.0/582+10);r.Right=r.Left+width;}else{height=(int)Math.Round((width-10)*582.0/741+38);if(edge==4||edge==5)r.Top=r.Bottom-height;else r.Bottom=r.Top+height;}Marshal.StructureToPtr(r,m.LParam,false);m.Result=(IntPtr)1;return;}base.WndProc(ref m);}
 void ShowMenu(){var menu=new ContextMenuStrip();menu.Items.Add("置顶",null,(s,e)=>TogglePin());menu.Items.Add("CTCS-3 设备",null,async(s,e)=>await SetDevice("CTCS-3",3));menu.Items.Add("CTCS-2 设备",null,async(s,e)=>await SetDevice("CTCS-2",2));menu.Items.Add("设置",null,(s,e)=>Process.Start("http://127.0.0.1:31271/settings.html#display"));menu.Items.Add("刷新",null,(s,e)=>web.Reload());menu.Items.Add("最小化",null,(s,e)=>WindowState=FormWindowState.Minimized);menu.Items.Add("关闭",null,(s,e)=>Close());menu.Closed+=(s,e)=>menu.Dispose();menu.Show(Cursor.Position);}

 async Task SetDevice(string device,int level){try{if(web.CoreWebView2!=null)await web.CoreWebView2.ExecuteScriptAsync("cfg.device='"+device+"';cfg.level="+level+";cfg.autoRoute=false;cfg.bezel=true;cfg.displayScale='fit';commit();render();");}catch(Exception e){MessageBox.Show(this,e.Message,"设备切换失败",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
 async Task Boot(){try{
  if(verify){bool before=TopMost;pinButton.PerformClick();bool pin=before!=TopMost;pinButton.PerformClick();minButton.PerformClick();bool min=WindowState==FormWindowState.Minimized;WindowState=FormWindowState.Normal;maxButton.PerformClick();bool max=WindowState==FormWindowState.Maximized;maxButton.PerformClick();bool restored=WindowState==FormWindowState.Normal;File.WriteAllText(Path.Combine(root,"work","native-button-check.txt"),"pin="+pin+";minimize="+min+";maximize="+max+";restore="+restored);if(!pin||!min||!max||!restored)throw new Exception("Native button check failed");}

  bool connected=false;try{using(var c=new WebClient()){await c.DownloadStringTaskAsync("http://127.0.0.1:31271/api/status");connected=true;}}catch{}
  if(!connected){Process.Start(new ProcessStartInfo(Path.Combine(root,"DMI-Web.exe"),"--headless"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});await Task.Delay(1200);}
  File.WriteAllText(Path.Combine(root,"work","overlay-stage.txt"),"Creating browser environment");
  var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"work",verify?"overlay-check-profile":"overlay-profile"),new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
  File.WriteAllText(Path.Combine(root,"work","overlay-stage.txt"),"Creating web control");
  await web.EnsureCoreWebView2Async(environment);File.WriteAllText(Path.Combine(root,"work","overlay-stage.txt"),"Web control ready");web.CoreWebView2.Settings.AreDefaultContextMenusEnabled=false;web.CoreWebView2.Settings.AreDevToolsEnabled=false;web.CoreWebView2.Settings.IsStatusBarEnabled=false;
  web.CoreWebView2.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
  web.CoreWebView2.WebResourceRequested+=async(s,e)=>{
   Uri uri;if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||uri.Host!="127.0.0.1"||uri.Port!=31271||uri.AbsolutePath=="/api/ato"){e.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Forbidden","");return;}
   var deferred=e.GetDeferral();try{
    var request=(HttpWebRequest)WebRequest.Create(uri);request.Proxy=null;request.Method=e.Request.Method;request.Timeout=8000;
    foreach(string header in new[]{"Origin","X-DMI-Token"})if(e.Request.Headers.Contains(header))request.Headers[header]=e.Request.Headers.GetHeader(header);
    if(request.Method=="POST"){request.ContentType="application/json";byte[] body;using(var buffer=new MemoryStream()){if(e.Request.Content!=null)e.Request.Content.CopyTo(buffer);body=buffer.ToArray();}request.ContentLength=body.Length;using(var output=await request.GetRequestStreamAsync())await output.WriteAsync(body,0,body.Length);}
    HttpWebResponse response;try{response=(HttpWebResponse)await request.GetResponseAsync();}catch(WebException ex){response=ex.Response as HttpWebResponse;if(response==null)throw;}
    using(response){var buffer=new MemoryStream();using(var input=response.GetResponseStream())await input.CopyToAsync(buffer);buffer.Position=0;e.Response=environment.CreateWebResourceResponse(buffer,(int)response.StatusCode,response.StatusDescription,"Content-Type: "+response.ContentType+"\r\nCache-Control: no-store\r\n");}
   }catch(Exception ex){e.Response=environment.CreateWebResourceResponse(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Bridge unavailable")),502,"Bad Gateway","Content-Type: text/plain");File.WriteAllText(Path.Combine(root,"work","overlay-transport-error.txt"),ex.Message);}finally{deferred.Complete();}
  };

  web.CoreWebView2.WebMessageReceived+=(s,e)=>{string message=e.TryGetWebMessageAsString();if(message=="menu"){ShowMenu();return;}if(message=="window:minimize"){WindowState=FormWindowState.Minimized;return;}if(message=="window:maximize"){MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;return;}if(message=="window:close"){Close();return;}if(message=="window:pin"){TopMost=!TopMost;web.CoreWebView2.PostWebMessageAsJson("{\"topmost\":"+(TopMost?"true":"false")+"}");return;}int hit;if(message.StartsWith("drag:")&&Int32.TryParse(message.Substring(5),out hit)&&(hit==2||(hit>=10&&hit<=17))){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)hit,IntPtr.Zero);}};
  web.CoreWebView2.NavigationStarting+=(s,e)=>{File.AppendAllText(Path.Combine(root,"work","overlay-stage.txt"),"\nStarting: "+e.Uri);if(e.Uri!="http://127.0.0.1:31271/overlay.html")e.Cancel=true;};
  web.CoreWebView2.NewWindowRequested+=(s,e)=>{e.Handled=true;if(e.Uri.StartsWith("http://127.0.0.1:31271/settings.html",StringComparison.Ordinal))Process.Start(e.Uri);};
  web.CoreWebView2.NavigationCompleted+=async(s,e)=>{try{File.AppendAllText(Path.Combine(root,"work","overlay-stage.txt"),"\nCompleted: "+e.IsSuccess+" "+e.WebErrorStatus);status.Text=e.IsSuccess?"拖动标题栏移动／边框缩放 · TSW 请使用无边框或窗口模式":"页面加载失败，请确认桥接后刷新";if(!e.IsSuccess)return;
   await web.CoreWebView2.ExecuteScriptAsync("cfg.bezel=true;cfg.displayScale='fit';engine.c.bezel=true;engine.c.displayScale='fit';sound.unlock();");
   if(verify){await Task.Delay(4500);
    string savedConfig=await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify(engine.c)");
    await SetDevice("CTCS-2",2);string c2=await web.CoreWebView2.ExecuteScriptAsync("engine.c.device==='CTCS-2'&&engine.c.level===2&&!engine.c.autoRoute");
    await SetDevice("CTCS-3",3);string c3=await web.CoreWebView2.ExecuteScriptAsync("engine.c.device==='CTCS-3'&&engine.c.level===3&&!engine.c.autoRoute");
    await web.CoreWebView2.ExecuteScriptAsync("cfg=JSON.parse("+savedConfig+");commit();render();");
    File.WriteAllText(Path.Combine(root,"work","overlay-system-check.txt"),"C2="+c2+";C3="+c3);if(c2!="true"||c3!="true")throw new Exception("Device switching verification failed");
    bool originalTop=TopMost;pinButton.PerformClick();bool pinOk=TopMost!=originalTop;pinButton.PerformClick();minButton.PerformClick();bool minOk=WindowState==FormWindowState.Minimized;WindowState=FormWindowState.Normal;maxButton.PerformClick();bool maxOk=WindowState==FormWindowState.Maximized;maxButton.PerformClick();bool restoreOk=WindowState==FormWindowState.Normal;
    File.WriteAllText(Path.Combine(root,"work","overlay-controls-check.txt"),"pin="+pinOk+";minimize="+minOk+";maximize="+maxOk+";restore="+restoreOk+";dragHandler=NativeMouseDown;resizeHandler=NativeMouseDown");if(!pinOk||!minOk||!maxOk||!restoreOk)throw new Exception("Native controls verification failed");await Task.Delay(300);string result="";
    await web.CoreWebView2.ExecuteScriptAsync("(async()=>{const sounds=[];for(const name of ['允许缓解','减速','制动完成','右侧站台','左侧站台','开机，动车提示音','控制速度']){try{const b=await sound.loadVoice(name);sounds.push({name,duration:b.duration})}catch(e){sounds.push({name,error:e.message})}}document.body.dataset.check=JSON.stringify({buttons:document.querySelectorAll('button').length,ato:!!document.getElementById('ato-panel'),error:document.getElementById('display-error').textContent,sounds})})()");
    for(int i=0;i<30;i++){await Task.Delay(300);result=await web.CoreWebView2.ExecuteScriptAsync("document.body.dataset.check||''");if(result!="\"\"")break;}
    File.WriteAllText(Path.Combine(root,"work","overlay-check.json"),result);File.WriteAllText(Path.Combine(root,"work","overlay-native-check.txt"),"TopMost="+TopMost+"; size="+ClientSize);using(var file=File.Create(Path.Combine(root,"work","overlay-preview.png")))await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);Close();}
  }catch(Exception ex){File.WriteAllText(Path.Combine(root,"work","overlay-error.txt"),ex.ToString());if(verify)Close();}};
  File.WriteAllText(Path.Combine(root,"work","overlay-stage.txt"),"Navigating to DMI");
  web.CoreWebView2.Navigate("http://127.0.0.1:31271/overlay.html");
 }catch(Exception ex){status.Text="悬浮窗启动失败："+ex.Message;Directory.CreateDirectory(Path.Combine(root,"work"));File.WriteAllText(Path.Combine(root,"work","overlay-error.txt"),ex.ToString());if(verify)Close();}}
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new DmiOverlay(Array.IndexOf(args,"--verify")>=0));}
}

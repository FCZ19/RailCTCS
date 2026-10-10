using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("RailCTCS 安装程序")]
[assembly: AssemblyProduct("RailCTCS")]
[assembly: AssemblyVersion("1.0.0.0")]
class Setup : Form {
 const string RegKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\RailCTCS";
 static readonly string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","RailCTCS");
 static readonly string Menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"RailCTCS");
 readonly Button install=new Button{Text="安装 RailCTCS",Width=140,Height=34};
 readonly Label message=new Label{AutoSize=false,Left=24,Top=68,Width=480,Height=130};
 readonly CheckBox launch=new CheckBox{Text="完成后打开桌面 DMI",Checked=true,Left=24,Top=210,Width=260};
 bool done;
 Setup(){Text="RailCTCS 安装程序";ClientSize=new Size(530,305);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",10);
  Controls.Add(new Label{Text="RailCTCS",Font=new Font("Microsoft YaHei UI",22,FontStyle.Bold),Left=22,Top=16,Width=460,Height=46});
  message.Text="安装桌面 DMI、TSW 桥接程序及音效。\r\n仅为当前用户安装，无需管理员权限。\r\n\r\n安装位置："+Target;Controls.Add(message);Controls.Add(launch);
  install.Left=360;install.Top=254;install.Click+=(s,e)=>Install();Controls.Add(install);
 }
 static string SafePath(string root,string relative){string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));if(!full.StartsWith(Path.GetFullPath(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("无效的安装文件路径");return full;}
 static void Extract(string root){Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"work"));using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){foreach(var entry in zip.Entries){if(entry.FullName.EndsWith("/"))continue;string path=SafePath(root,entry.FullName);Directory.CreateDirectory(Path.GetDirectoryName(path));using(var input=entry.Open())using(var output=File.Create(path))input.CopyTo(output);}}}
 static void Shortcut(string path,string exe,string args,string root){var type=Type.GetTypeFromProgID("WScript.Shell");dynamic shell=Activator.CreateInstance(type);dynamic link=shell.CreateShortcut(path);link.TargetPath=exe;link.Arguments=args;link.WorkingDirectory=root;link.Description="RailCTCS";link.Save();}
 static void CheckClosed(string root){foreach(var name in new[]{"DMI-Overlay","DMI-Web"})foreach(var process in Process.GetProcessesByName(name)){try{if(process.MainModule.FileName.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("请先关闭已安装的 RailCTCS 桌面 DMI 和桥接程序，再重试。");}catch(System.ComponentModel.Win32Exception){throw new InvalidOperationException("无法确认运行中的程序位置，请先关闭 DMI 和桥接程序再重试。");}}}
 void Install(){if(done){if(launch.Checked)Process.Start(new ProcessStartInfo(Path.Combine(Target,"overlay","DMI-Overlay.exe")){WorkingDirectory=Target});Close();return;}install.Enabled=false;try{
   CheckClosed(Target);Extract(Target);File.Copy(Application.ExecutablePath,Path.Combine(Target,"Uninstall.exe"),true);Directory.CreateDirectory(Menu);
   Shortcut(Path.Combine(Menu,"RailCTCS 桌面 DMI.lnk"),Path.Combine(Target,"overlay","DMI-Overlay.exe"),"",Target);
   Shortcut(Path.Combine(Menu,"RailCTCS 桥接程序.lnk"),Path.Combine(Target,"DMI-Web.exe"),"",Target);
   Shortcut(Path.Combine(Menu,"卸载 RailCTCS.lnk"),Path.Combine(Target,"Uninstall.exe"),"--uninstall",Target);
   Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"RailCTCS.lnk"),Path.Combine(Target,"overlay","DMI-Overlay.exe"),"",Target);
   File.WriteAllText(Path.Combine(Menu,"RailCTCS 设置.url"),"[InternetShortcut]\r\nURL=http://127.0.0.1:31271/settings.html\r\n");
   using(var key=Registry.CurrentUser.CreateSubKey(RegKey)){key.SetValue("DisplayName","RailCTCS");key.SetValue("DisplayVersion","1.0.0");key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",Path.Combine(Target,"overlay","DMI-Overlay.exe"));key.SetValue("UninstallString","\""+Path.Combine(Target,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
   message.Text="RailCTCS 安装完成。\r\n\r\n桌面快捷方式：RailCTCS\r\n开始菜单包含桌面 DMI、桥接程序和设置入口。\r\n打开 DMI 时会自动连接或启动桥接程序。";done=true;install.Text="完成";
  }catch(Exception ex){MessageBox.Show(this,ex.Message,"安装未完成",MessageBoxButtons.OK,MessageBoxIcon.Error);}finally{install.Enabled=true;}}
 static void RemoveFiles(string root){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){foreach(var entry in zip.Entries){if(entry.FullName.EndsWith("/"))continue;File.Delete(SafePath(root,entry.FullName));}}
 }
 static void Uninstall(){if(MessageBox.Show("卸载 RailCTCS 程序？用户设置和运行记录将保留。","RailCTCS",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;CheckClosed(Target);string temp=Path.Combine(Path.GetTempPath(),"RailCTCS-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Application.ExecutablePath,temp);Process.Start(new ProcessStartInfo(temp,"--remove"){UseShellExecute=false});}
 [STAThread]static void Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);try{
   if(args.Length==2&&args[0]=="--verify-payload"){string root=Path.GetFullPath(args[1]);if(Directory.Exists(root))throw new Exception("测试目录必须不存在");Extract(root);if(!File.Exists(Path.Combine(root,"DMI-Web.exe"))||!File.Exists(Path.Combine(root,"overlay","DMI-Overlay.exe"))||!File.Exists(Path.Combine(root,"dist","overlay.html")))throw new Exception("安装文件不完整");File.WriteAllText(root+".verified","Payload extracted successfully");return;}
   if(args.Length>0&&args[0]=="--uninstall"){Uninstall();return;}
   if(args.Length>0&&args[0]=="--remove"){CheckClosed(Target);RemoveFiles(Target);File.Delete(Path.Combine(Target,"Uninstall.exe"));foreach(string name in new[]{"RailCTCS 桌面 DMI.lnk","RailCTCS 桥接程序.lnk","卸载 RailCTCS.lnk","RailCTCS 设置.url"})File.Delete(Path.Combine(Menu,name));if(Directory.Exists(Menu)&&Directory.GetFileSystemEntries(Menu).Length==0)Directory.Delete(Menu);File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"RailCTCS.lnk"));Registry.CurrentUser.DeleteSubKeyTree(RegKey,false);MessageBox.Show("RailCTCS 已卸载。用户设置和运行记录已保留。","RailCTCS");return;}
   Application.Run(new Setup());
  }catch(Exception ex){if(args.Length>0&&args[0]=="--verify-payload"){File.WriteAllText(args[1]+".failed",ex.ToString());Environment.ExitCode=1;}else MessageBox.Show(ex.Message,"RailCTCS",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}

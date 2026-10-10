# RailCTCS

连接 Train Sim World 的列控 DMI 仿真项目，包含网页端、本机桥接和可置顶的 Windows 桌面 DMI。

## 安装与使用

运行 `releases/RailCTCS-Setup.exe`，按当前用户安装。桌面快捷方式启动 DMI，并自动启动或连接桥接。

需要 Windows x64、.NET Framework 4.5+ 和 Microsoft Edge WebView2 Runtime。TSW 需要启用 HTTP API；司机室激活后 DMI 启动。

- 设置页面：http://127.0.0.1:31271/settings.html
- 标题栏 C2/C3 切换设备；DMI F2 选择运行模式，F6 确认。
- 桌面窗口支持置顶、拖动、比例缩放和最小化，不包含 ATO 控件。
- 网页端包括摇杆映射和 ATO 功能。
- 速度为 0 时不产生超速提示或新的超速制动；已有制动锁存和警惕确认仍保留。

## 构建

```powershell
./Build.ps1
./Build-Overlay.ps1
./installer/Build-Installer.ps1
node --test tests/*.test.js
```

`src/` 是 C# 桥接和窗口源码；`dist/` 是网页及音效；`tests/` 是测试；`installer/` 是安装器源码。

本机授权、连接配置、线路配置、WebView 用户数据及运行缓存不纳入版本管理。

## 第三方资源

WebView2 许可见 `overlay/WebView2-LICENSE.txt`。提示音来自 RailBridge、LKJ 网站及用户提供的音效，保留在各音效目录的来源清单中，相关权利归原作者；本项目未为这些资源另行授予许可。

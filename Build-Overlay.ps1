$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$lib=Join-Path $PSScriptRoot 'overlay'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$lib/DMI-Overlay.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/reference:$lib/Microsoft.Web.WebView2.Core.dll" "/reference:$lib/Microsoft.Web.WebView2.WinForms.dll" (Join-Path $PSScriptRoot 'src/Overlay.cs')
if($LASTEXITCODE -ne 0){throw 'Overlay build failed'}

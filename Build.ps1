$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework C# compiler not found.' }
$sourceApi = Join-Path $PSScriptRoot 'src\ApiCore.cs'
$sourceBridge = Join-Path $PSScriptRoot 'src\Bridge.cs'
$sourceControl = Join-Path $PSScriptRoot 'src\Control.cs'
$sourceNative = Join-Path $PSScriptRoot 'src\NativeJoystick.cs'
$sourceCalibration = Join-Path $PSScriptRoot 'src\Calibration.cs'
$sourceAto = Join-Path $PSScriptRoot 'src\Ato.cs'
$sourceXInput = Join-Path $PSScriptRoot 'src\XInputDevices.cs'
$outputPath = Join-Path $PSScriptRoot 'DMI-Web.exe'
& $compilerPath /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 "/out:$outputPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll $sourceApi $sourceBridge $sourceControl $sourceCalibration $sourceNative $sourceAto $sourceXInput
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built: $outputPath"

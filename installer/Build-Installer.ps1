$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$build=Join-Path $root 'work/installer-build'
New-Item -ItemType Directory -Force $build | Out-Null
$payload=Join-Path $build 'payload.zip'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream=[IO.File]::Open($payload,[IO.FileMode]::Create)
$zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
 foreach($folder in @('dist','overlay')) {
  Get-ChildItem -LiteralPath (Join-Path $root $folder) -Recurse -File | ForEach-Object {
   $name=$_.FullName.Substring($root.Length+1).Replace('\','/')
   [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$_.FullName,$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
 }
 foreach($name in @('DMI-Web.exe','中文使用说明.txt','RailBridge音效来源.json')) {
  [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $root $name),$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
 }
} finally {$zip.Dispose();$stream.Dispose()}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
New-Item -ItemType Directory -Force (Join-Path $root 'releases') | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$root/releases/RailCTCS-Setup.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:Microsoft.CSharp.dll "/resource:$payload,payload.zip" (Join-Path $PSScriptRoot 'Setup.cs')
if($LASTEXITCODE -ne 0){throw 'RailCTCS installer build failed'}

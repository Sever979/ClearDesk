$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = Split-Path $PSScriptRoot -Parent
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$output\ClearDesk.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/resource:$output\LICENSE.txt,LICENSE.txt" "$PSScriptRoot\ClearDesk.cs" "$PSScriptRoot\ProductInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/out:$output\ClearDesk-Setup.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/resource:$output\ClearDesk.exe,ClearDesk.exe" "/resource:$output\LICENSE.txt,LICENSE.txt" "$PSScriptRoot\Setup.cs" "$PSScriptRoot\ProductInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }

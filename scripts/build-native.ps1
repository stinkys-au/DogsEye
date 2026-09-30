param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (!(Test-Path $vswhere)) { throw 'Install Visual Studio Build Tools with Desktop development with C++.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$installation) { throw 'MSVC x64 build tools are missing.' }
$nativeOutput = Join-Path $projectRoot 'artifacts\native'
New-Item -ItemType Directory -Force -Path $nativeOutput | Out-Null
$source = Join-Path $projectRoot 'native\DogsEye.Tobii.cpp'
$include = Join-Path $projectRoot 'sdk\include'
$library = Join-Path $projectRoot 'sdk\lib\tobii_gameintegration_x64.lib'
if (!(Test-Path $library)) { throw 'Place the Tobii Game Integration SDK in sdk first.' }
$devcmd = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
$compile = 'call "{0}" -arch=x64 -host_arch=x64 >nul && cl /nologo /std:c++17 /EHsc /W4 /WX /O2 /MT /LD /I"{1}" "{2}" "{3}" /link /OUT:"{4}\DogsEye.Tobii.dll"' -f $devcmd, $include, $source, $library, $nativeOutput
Push-Location $nativeOutput
try { & $env:ComSpec /d /s /c $compile; if ($LASTEXITCODE -ne 0) { throw 'Native adapter build failed.' } }
finally { Pop-Location }
Copy-Item (Join-Path $projectRoot 'sdk\lib\tobii_gameintegration_x64.dll') $nativeOutput -Force

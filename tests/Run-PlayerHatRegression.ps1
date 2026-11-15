param([string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2')
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
dotnet run --project (Join-Path $PSScriptRoot 'PlayerHatRegression.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Player hat regression failed' }
dotnet build (Join-Path $PSScriptRoot 'PlayerHatNativeRegression.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Native hat codec build failed' }
$outputDir = Join-Path $repoDir 'artifacts/player-hat-regression'
New-Item -ItemType Directory -Force $outputDir | Out-Null
$hostExe = Join-Path $outputDir 'MonoHost.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $repoDir 'tools/research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
& $hostExe $GameDir (Join-Path $PSScriptRoot 'bin/player-hat-native/PlayerHatNativeRegression.dll')
if ($LASTEXITCODE -ne 0) { throw 'Native hat codec regression failed' }

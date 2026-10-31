param([string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2')
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDir = Join-Path $repoDir 'artifacts/cache-mono-regression'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
dotnet build (Join-Path $PSScriptRoot 'DiskCacheMonoRegression.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Mono cache regression build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $outputDir 'MonoCacheRegressionHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $repoDir 'tools/research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
& $hostExe $GameDir (Join-Path $PSScriptRoot 'bin/Release/DIYChef.CacheMonoRegression.dll')
if ($LASTEXITCODE -ne 0) { throw 'Mono cache regression failed' }

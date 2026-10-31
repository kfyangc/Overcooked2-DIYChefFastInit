param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$ResourceDir = '',
    [string]$CacheDir = ''
)
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$outputDir = Join-Path $repoDir 'artifacts/chef-switch-research'
New-Item -ItemType Directory -Force $outputDir | Out-Null
if (!$ResourceDir) { $ResourceDir = Join-Path $GameDir 'BepInEx/plugins/OC2DIYChef-厨房暖暖/Resources/144-Anya' }
if (!$CacheDir) { $CacheDir = Join-Path $GameDir 'BepInEx/cache/DIYChefFastInit' }
# The cache must already exist. Use a chef with decoded texture and mesh caches.
$env:DIYCHEF_PROBE_RESOURCE_DIR = $ResourceDir
$env:DIYCHEF_PROBE_CACHE_DIR = $CacheDir
$env:DIYCHEF_PROBE_REPORT = Join-Path $outputDir 'mono-benchmark.csv'
dotnet build (Join-Path $PSScriptRoot 'MonoCacheProbe.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Managed probe build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $outputDir 'MonoCacheHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $PSScriptRoot 'MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
# Loads only the embedded Mono runtime and a managed I/O probe. No UnityPlayer or game executable.
& $hostExe $GameDir (Join-Path $outputDir 'bin/Release/DIYChef.CacheProbe.dll')
if ($LASTEXITCODE -ne 0) { throw 'Mono probe failed' }

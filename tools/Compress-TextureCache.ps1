param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$CacheDir = ''
)
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDir = Join-Path $repoDir 'artifacts/texture-cache-converter'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
if (!$CacheDir) { $CacheDir = Join-Path $GameDir 'BepInEx/cache/DIYChefFastInit' }
$env:DIYCHEF_TEXTURE_CACHE_ROOT = [IO.Path]::GetFullPath($CacheDir)
$env:DIYCHEF_TEXTURE_CACHE_REPORT = Join-Path $outputDir 'conversion-result.txt'
dotnet build (Join-Path $PSScriptRoot 'texture-cache-converter/TextureCacheConverter.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Texture converter build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $outputDir 'TextureCacheConverterHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $PSScriptRoot 'research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
# Embedded Mono plus file/codec APIs only. Does not launch the game or load UnityPlayer.
& $hostExe $GameDir (Join-Path $outputDir 'bin/Release/DIYChef.TextureCacheConverter.dll')
if ($LASTEXITCODE -ne 0) { throw 'Texture cache conversion failed' }

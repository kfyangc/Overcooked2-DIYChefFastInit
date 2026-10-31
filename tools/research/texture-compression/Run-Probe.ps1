param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$SamplesDir = '',
    [string]$Lz4Dll = '',
    [string]$ZstdDll = ''
)
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (!$SamplesDir) { $SamplesDir = Join-Path $repoDir 'artifacts/texture-compression' }
$env:DIYCHEF_COMPRESSION_DIR = [IO.Path]::GetFullPath($SamplesDir)
if ($Lz4Dll) { $env:DIYCHEF_LZ4_PROBE_DLL = [IO.Path]::GetFullPath($Lz4Dll) }
if ($ZstdDll) { $env:DIYCHEF_ZSTD_PROBE_DLL = [IO.Path]::GetFullPath($ZstdDll) }
dotnet build (Join-Path $PSScriptRoot 'MonoTextureCompressionProbe.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Compression probe build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $SamplesDir 'MonoTextureHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $PSScriptRoot '../chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
# Only loads the embedded Mono runtime; no UnityPlayer and no game executable.
& $hostExe $GameDir (Join-Path $repoDir 'artifacts/texture-compression/bin/Release/DIYChef.TextureCompressionProbe.dll')
if ($LASTEXITCODE -ne 0) { throw 'Compression probe failed' }

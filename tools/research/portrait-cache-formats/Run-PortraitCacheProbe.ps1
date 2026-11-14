param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$SourceDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$gameRoot = [IO.Path]::GetFullPath($GameDir)
if ([string]::IsNullOrEmpty($SourceDirectory)) {
    $SourceDirectory = Join-Path $gameRoot 'BepInEx/cache/DIYChefFastInit/portraits'
}
$sourceRoot = [IO.Path]::GetFullPath($SourceDirectory)
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) { throw "Missing portrait source: $sourceRoot" }
$outputDir = Join-Path $repoDir 'artifacts/portrait-cache-formats'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

# This host initializes the game's Mono DLL only; no game executable or Unity scene runs.
dotnet build (Join-Path $PSScriptRoot 'MonoPortraitCacheProbe.csproj') -c Release "-p:GameDir=$gameRoot" "-p:BaseIntermediateOutputPath=$outputDir/obj/"
if ($LASTEXITCODE -ne 0) { throw 'Portrait cache probe build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $outputDir 'MonoPortraitCacheHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $repoDir 'tools/research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }

$oldSource = $env:DIYCHEF_PORTRAIT_SOURCE
$oldOutput = $env:DIYCHEF_PORTRAIT_REPORT_DIR
try {
    $env:DIYCHEF_PORTRAIT_SOURCE = $sourceRoot
    $env:DIYCHEF_PORTRAIT_REPORT_DIR = $outputDir
    & $hostExe $gameRoot (Join-Path $outputDir 'bin/Release/DIYChef.PortraitCacheProbe.dll') |
        Tee-Object -FilePath (Join-Path $outputDir 'probe.txt')
    if ($LASTEXITCODE -ne 0) { throw 'Portrait cache probe failed' }
} finally {
    $env:DIYCHEF_PORTRAIT_SOURCE = $oldSource
    $env:DIYCHEF_PORTRAIT_REPORT_DIR = $oldOutput
}

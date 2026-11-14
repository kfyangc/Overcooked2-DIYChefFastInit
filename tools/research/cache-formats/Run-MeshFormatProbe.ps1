param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$ResourcesDir
)

$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$GameDir = [IO.Path]::GetFullPath($GameDir)
if ([string]::IsNullOrEmpty($ResourcesDir)) {
    $ResourcesDir = Join-Path $GameDir 'BepInEx/plugins/OC2DIYChef-FastInit/Resources'
}
$ResourcesDir = [IO.Path]::GetFullPath($ResourcesDir)
if (!(Test-Path -LiteralPath $ResourcesDir -PathType Container)) { throw "Missing resource directory: $ResourcesDir" }
if (!(Test-Path -LiteralPath (Join-Path $GameDir 'Overcooked2_Data/Mono/EmbedRuntime/mono.dll'))) {
    throw "Missing game Mono runtime: $GameDir"
}

# Fixed artifact root: cache files are never written into the game installation.
$artifactDir = Join-Path $repoDir 'artifacts/cache-formats'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
$intermediateDir = Join-Path $artifactDir 'obj/'
dotnet build (Join-Path $PSScriptRoot 'MeshFormatProbe.csproj') -c Release "-p:GameDir=$GameDir" "-p:BaseIntermediateOutputPath=$intermediateDir"
if ($LASTEXITCODE -ne 0) { throw 'Mesh format probe build failed' }

$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $artifactDir 'MonoCacheHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $repoDir 'tools/research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Shared x86 Mono host build failed' }

$runDir = Join-Path $artifactDir ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDir | Out-Null
$previousResources = $env:DIYCHEF_MESH_PROBE_RESOURCES
$previousOutput = $env:DIYCHEF_MESH_PROBE_OUTPUT
try {
    $env:DIYCHEF_MESH_PROBE_RESOURCES = $ResourcesDir
    $env:DIYCHEF_MESH_PROBE_OUTPUT = $runDir
    & $hostExe $GameDir (Join-Path $artifactDir 'bin/Release/DIYChef.MeshFormatProbe.dll')
    if ($LASTEXITCODE -ne 0) { throw "Mesh format probe failed. Results: $runDir" }
    $reportPath = Join-Path $runDir 'mesh-format-results.txt'
    if (!(Test-Path -LiteralPath $reportPath) -or
        !(Select-String -LiteralPath $reportPath -SimpleMatch 'PASS: no Unity objects created;' -Quiet)) {
        throw "Mesh format probe did not pass. Results: $runDir"
    }
    Write-Output "Results: $reportPath"
}
finally {
    $env:DIYCHEF_MESH_PROBE_RESOURCES = $previousResources
    $env:DIYCHEF_MESH_PROBE_OUTPUT = $previousOutput
}

param([string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2')
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputDir = Join-Path $repoDir 'artifacts/portrait-cache-regression'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
dotnet build (Join-Path $PSScriptRoot 'PortraitCacheMonoRegression.csproj') -c Release "-p:GameDir=$GameDir"
if ($LASTEXITCODE -ne 0) { throw 'Portrait cache Mono regression build failed' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$hostExe = Join-Path $outputDir 'MonoCacheRegressionHost.exe'
& $compiler /nologo /target:exe /platform:x86 "/out:$hostExe" (Join-Path $repoDir 'tools/research/chef-cache-probe/MonoCacheHost.cs')
if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
Push-Location $repoDir
try {
    & $hostExe $GameDir (Join-Path $PSScriptRoot 'bin/portrait-cache-mono/DIYChef.PortraitCacheMonoRegression.dll')
    if ($LASTEXITCODE -ne 0) { throw 'Portrait cache Mono regression failed' }
} finally { Pop-Location }

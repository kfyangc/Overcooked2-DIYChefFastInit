param(
    [string]$GameDir = 'D:/Games/Steam Games/steamapps/common/Overcooked! 2',
    [string]$ResourceDir = ''
)
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path $PSScriptRoot -Parent
$baselinePath = Join-Path $PSScriptRoot 'obj/BaselineObjImporter.cs'
New-Item -ItemType Directory -Force (Split-Path $baselinePath) | Out-Null
$baselineSource = git -C $repoDir show a7245bbf97828969fe87e198fd54003ae83c40f5:ObjImporter.cs
if ($LASTEXITCODE -ne 0) { throw 'Cannot read original OBJ importer from Git.' }
($baselineSource -join "`n").Replace('public static class ObjImporter', 'public static class BaselineObjImporter') |
    Set-Content -LiteralPath $baselinePath -Encoding utf8
if (!$ResourceDir) { $ResourceDir = Join-Path $GameDir 'BepInEx/plugins/OC2DIYChef-厨房暖暖/Resources' }
dotnet run --project (Join-Path $PSScriptRoot 'ObjImportRegression.csproj') -c Release "-p:GameDir=$GameDir" -- $ResourceDir
if ($LASTEXITCODE -ne 0) { throw 'Offline OBJ regression failed.' }

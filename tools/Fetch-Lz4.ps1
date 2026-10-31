param([string]$OutputDir = '')
$ErrorActionPreference = 'Stop'
$repoDir = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (!$OutputDir) { $OutputDir = Join-Path $repoDir 'lib' }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$archivePath = Join-Path $OutputDir 'lz4-win32.zip'
$archiveHash = 'C0321A555F522DF4FAD8D24FA6C53B4A8B65EAEBC00D40FB913803BC5D646282'
$dllHash = '803075F5021D5A1EB0B7FA75C6BB1675DDB01F1F4FC99F6E118A62F2B594C6C2'
$dllPath = Join-Path $OutputDir 'lz4-win-x86.dll'
if ((Test-Path -LiteralPath $dllPath) -and (Get-FileHash -LiteralPath $dllPath).Hash -eq $dllHash) {
    Write-Output $dllPath
    return
}
if (!(Test-Path -LiteralPath $archivePath) -or (Get-FileHash -LiteralPath $archivePath).Hash -ne $archiveHash) {
    Invoke-WebRequest -Uri 'https://github.com/lz4/lz4/releases/download/v1.9.4/lz4_win32_v1_9_4.zip' -OutFile $archivePath
}
if ((Get-FileHash -LiteralPath $archivePath).Hash -ne $archiveHash) { throw 'LZ4 archive checksum mismatch' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entry = $archive.GetEntry('dll/msys-lz4-1.dll')
    if (!$entry) { throw 'LZ4 native DLL missing from archive' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $dllPath, $true)
    if ((Get-FileHash -LiteralPath $dllPath).Hash -ne $dllHash) { throw 'LZ4 DLL checksum mismatch' }
    Write-Output $dllPath
} finally { $archive.Dispose() }

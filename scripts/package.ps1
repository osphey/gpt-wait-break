[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $repoRoot 'tests\run.ps1')
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Invalid plugin version.' }
$packageName = "gpt-wait-break-$version-plugin"
$distPath = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$stagingPath = Join-Path $distPath ($packageName + '-' + [Guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $distPath ($packageName + '.zip')
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
$items = @('plugin.json', '.codex-plugin', '.agents', 'marketplace.json', 'hooks', 'skills', 'docs', 'README.md', 'CHANGELOG.md', 'LICENSE', 'Install.ps1', 'scripts', 'tests')
foreach ($item in $items) {
    $sourcePath = Join-Path $repoRoot $item
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Package file is missing: $item" }
    Copy-Item -LiteralPath $sourcePath -Destination $stagingPath -Recurse -Force
}
$unexpectedFiles = @(Get-ChildItem -LiteralPath $stagingPath -File -Recurse | Where-Object { $_.Extension -in @('.exe', '.dll', '.cs', '.pdb', '.nupkg') })
if ($unexpectedFiles.Count -gt 0) { throw 'Plugin package contains unexpected compiled or legacy files.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporaryZip = Join-Path $distPath ([Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($stagingPath, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Move-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
Write-Host "Package: $zipPath"
Write-Host "SHA256: $checksum"

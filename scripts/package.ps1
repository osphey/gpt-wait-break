[CmdletBinding()]
param([switch]$SkipBuild, [switch]$Offline)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') -Offline:$Offline }
& (Join-Path $repoRoot 'tests\run.ps1') -SkipBuild
$version = (Get-Content -LiteralPath (Join-Path $repoRoot 'plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'Plugin version is not a safe semantic version.' }
$packageName = "gpt-wait-break-$version-win-x64"
$distPath = [IO.Path]::GetFullPath((Join-Path $repoRoot 'dist'))
$stagingPath = [IO.Path]::GetFullPath((Join-Path $distPath $packageName))
$zipPath = Join-Path $distPath ($packageName + '.zip')
if ([IO.Path]::GetDirectoryName($stagingPath) -ne $distPath -or [IO.Path]::GetFileName($stagingPath) -ne $packageName) { throw 'Unexpected staging path.' }
New-Item -ItemType Directory -Path $distPath -Force | Out-Null
if (Test-Path -LiteralPath $stagingPath) {
    if ((Get-Item -LiteralPath $stagingPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Refusing to remove staging reparse point.' }
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingPath | Out-Null
$items = @('plugin.json', '.codex-plugin', '.agents', 'marketplace.json', 'hooks', 'skills', 'docs', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Install.ps1', 'Uninstall.ps1', 'scripts', 'src', 'tests')
foreach ($item in $items) {
    $sourcePath = Join-Path $repoRoot $item
    if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Package file is missing: $sourcePath" }
    Copy-Item -LiteralPath $sourcePath -Destination $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Path (Join-Path $stagingPath 'bin') -Force | Out-Null
foreach ($requiredFile in @('GptWaitBreak.exe', 'GptWaitBreak.exe.config', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'WebView2.LICENSE.txt', 'WebView2.NOTICE.txt')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot ('bin\' + $requiredFile)) -Destination (Join-Path $stagingPath 'bin') -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stagingPath ('bin\' + $requiredFile)) -PathType Leaf)) { throw "Release binary is missing: $requiredFile" }
}
Get-ChildItem -LiteralPath (Join-Path $stagingPath 'bin') -Filter '*.pdb' -File | Remove-Item -Force
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stagingPath, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
"$checksum  $([IO.Path]::GetFileName($zipPath))" | Set-Content -LiteralPath ($zipPath + '.sha256') -Encoding ASCII
Write-Host "Package: $zipPath"
Write-Host "SHA256: $checksum"

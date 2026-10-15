[CmdletBinding()]
param([switch]$Offline)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'Build requires Windows x64 and .NET Framework 4.8.'
}
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerPath = Join-Path $frameworkRoot 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
    throw '.NET Framework x64 C# compiler was not found. Install .NET Framework 4.8.'
}
$dependency = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json).webview2
$cachePath = Join-Path $repoRoot '.cache'
$binPath = Join-Path $repoRoot 'bin'
New-Item -ItemType Directory -Path $cachePath, $binPath -Force | Out-Null
$packagePath = Join-Path $cachePath ("webview2.{0}.nupkg" -f $dependency.version)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    if ($Offline) { throw "Offline dependency is missing: $packagePath" }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "Downloading pinned Microsoft.Web.WebView2 $($dependency.version)..."
    Invoke-WebRequest -Uri $dependency.url -OutFile $packagePath -UseBasicParsing
}
$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $dependency.sha256) {
    throw "WebView2 package SHA256 mismatch. Remove the package and retry. Expected $($dependency.sha256); actual $actualHash."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageArchive = [IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $entries = @{
        'lib/net462/Microsoft.Web.WebView2.Core.dll' = 'Microsoft.Web.WebView2.Core.dll'
        'lib/net462/Microsoft.Web.WebView2.WinForms.dll' = 'Microsoft.Web.WebView2.WinForms.dll'
        'build/native/x64/WebView2Loader.dll' = 'WebView2Loader.dll'
        'LICENSE.txt' = 'WebView2.LICENSE.txt'
        'NOTICE.txt' = 'WebView2.NOTICE.txt'
    }
    foreach ($entryName in $entries.Keys) {
        $entry = $packageArchive.GetEntry($entryName)
        if ($null -eq $entry) { throw "Pinned package is missing $entryName" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $binPath $entries[$entryName]), $true)
    }
} finally { $packageArchive.Dispose() }

$references = @(
    (Join-Path $frameworkRoot 'System.dll'),
    (Join-Path $frameworkRoot 'System.Core.dll'),
    (Join-Path $frameworkRoot 'System.Drawing.dll'),
    (Join-Path $frameworkRoot 'System.Windows.Forms.dll'),
    (Join-Path $frameworkRoot 'System.Web.Extensions.dll'),
    (Join-Path $frameworkRoot 'WPF\UIAutomationClient.dll'),
    (Join-Path $frameworkRoot 'WPF\UIAutomationTypes.dll'),
    (Join-Path $frameworkRoot 'WPF\WindowsBase.dll'),
    (Join-Path $binPath 'Microsoft.Web.WebView2.Core.dll'),
    (Join-Path $binPath 'Microsoft.Web.WebView2.WinForms.dll')
)
$sourceFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src') -Filter '*.cs' -File -Recurse | Sort-Object FullName | Select-Object -ExpandProperty FullName)
if ($sourceFiles.Count -eq 0) { throw 'No C# source files were found.' }
$compilerArguments = @('/nologo', '/target:winexe', '/platform:x64', '/langversion:5', '/optimize+', '/debug:pdbonly', '/utf8output')
$compilerArguments += '/out:' + (Join-Path $binPath 'GptWaitBreak.exe')
$compilerArguments += '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
$compilerArguments += $references | ForEach-Object { '/reference:' + $_ }
$compilerArguments += $sourceFiles
& $compilerPath @compilerArguments
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE." }

@'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup useLegacyV2RuntimeActivationPolicy="true">
    <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" />
  </startup>
</configuration>
'@ | Set-Content -LiteralPath (Join-Path $binPath 'GptWaitBreak.exe.config') -Encoding UTF8
Write-Host "Built: $(Join-Path $binPath 'GptWaitBreak.exe')"

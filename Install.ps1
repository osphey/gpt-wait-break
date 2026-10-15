[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$CodexPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'GPT Wait Break requires Windows.' }
if ($CodexPath) {
    if (-not (Test-Path -LiteralPath $CodexPath -PathType Leaf)) {
        throw "Codex CLI file was not found: $CodexPath"
    }
    $CodexPath = (Resolve-Path -LiteralPath $CodexPath).ProviderPath
} else {
    $codexCommand = Get-Command codex -CommandType Application, ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($codexCommand) { $CodexPath = $codexCommand.Source }
    if (-not $CodexPath) {
        $localRoots = @($env:LOCALAPPDATA)
        if ($env:USERPROFILE) { $localRoots += Join-Path $env:USERPROFILE 'AppData\Local' }
        $candidates = @()
        foreach ($localRoot in @($localRoots | Where-Object { $_ } | Select-Object -Unique)) {
            $bundledRoot = Join-Path $localRoot 'OpenAI\Codex\bin'
            $candidates += Join-Path $bundledRoot 'codex.exe'
            $candidates += @(Get-ChildItem -LiteralPath $bundledRoot -Directory -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending |
                ForEach-Object { Join-Path $_.FullName 'codex.exe' })
        }
        $CodexPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $CodexPath) {
    throw 'Codex CLI was not found. Install the Codex desktop app or provide -CodexPath with an existing codex.exe path.'
}
$pluginRoot = [IO.Path]::GetFullPath($PSScriptRoot)
foreach ($requiredFile in @('plugin.json', 'hooks\hooks.json', 'scripts\hook.ps1', 'scripts\config.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $requiredFile) -PathType Leaf)) {
        throw "Plugin file is missing: $requiredFile"
    }
}
if ($PSCmdlet.ShouldProcess('Codex plugins', 'Register GPT Wait Break marketplace and install the plugin')) {
    & $CodexPath plugin marketplace add $pluginRoot
    if ($LASTEXITCODE -ne 0) { throw 'Marketplace registration failed.' }
    & $CodexPath plugin add 'gpt-wait-break@gpt-wait-break-local'
    if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed.' }
    Write-Host 'Installed. Restart the desktop app, enable the plugin, and review/trust its hooks.'
}

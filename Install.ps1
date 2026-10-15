[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'GPT Wait Break requires Windows.' }
if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw 'codex CLI was not found. See docs\INSTALL.md for repo marketplace installation.'
}
$pluginRoot = [IO.Path]::GetFullPath($PSScriptRoot)
foreach ($requiredFile in @('plugin.json', 'hooks\hooks.json', 'scripts\hook.ps1', 'scripts\config.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $requiredFile) -PathType Leaf)) {
        throw "Plugin file is missing: $requiredFile"
    }
}
if ($PSCmdlet.ShouldProcess('Codex plugins', 'Register GPT Wait Break marketplace and install the plugin')) {
    & codex plugin marketplace add $pluginRoot
    if ($LASTEXITCODE -ne 0) { throw 'Marketplace registration failed.' }
    & codex plugin add 'gpt-wait-break@gpt-wait-break-local'
    if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed.' }
    Write-Host 'Installed. Restart the desktop app, enable the plugin, and review/trust its hooks.'
}

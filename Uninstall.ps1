[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RemovePlugin,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'This uninstaller is for Windows.' }
$localAppDataRoot = [IO.Path]::GetFullPath($env:LOCALAPPDATA)
$installRoot = [IO.Path]::GetFullPath((Join-Path $localAppDataRoot 'Programs\GptWaitBreak'))
$dataRoot = [IO.Path]::GetFullPath((Join-Path $localAppDataRoot 'GptWaitBreak'))

function Assert-ExactChildPath([string]$Target, [string]$Parent, [string]$ExpectedLeaf) {
    $resolvedTarget = [IO.Path]::GetFullPath($Target).TrimEnd('\')
    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($resolvedTarget) -ne $resolvedParent -or [IO.Path]::GetFileName($resolvedTarget) -ne $ExpectedLeaf) {
        throw "Refusing to remove unexpected path: $resolvedTarget"
    }
    if ((Test-Path -LiteralPath $resolvedTarget) -and ((Get-Item -LiteralPath $resolvedTarget -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to remove a reparse point: $resolvedTarget"
    }
}
Assert-ExactChildPath $installRoot (Join-Path $localAppDataRoot 'Programs') 'GptWaitBreak'
Assert-ExactChildPath $dataRoot $localAppDataRoot 'GptWaitBreak'
if (@(Get-Process -Name GptWaitBreak -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'Quit GPT Wait Break from its tray menu before uninstalling.'
}
if ($RemovePlugin) {
    if (-not (Get-Command codex -ErrorAction SilentlyContinue)) { throw 'codex CLI was not found. Remove the plugin in the desktop app first, then retry without -RemovePlugin.' }
    if ($PSCmdlet.ShouldProcess('Codex user plugin configuration', 'Remove GPT Wait Break plugin and its local marketplace')) {
        & codex plugin remove 'gpt-wait-break@gpt-wait-break-local'
        if ($LASTEXITCODE -ne 0) { throw 'Plugin removal failed. Remove it in the desktop app and retry without -RemovePlugin.' }
        & codex plugin marketplace remove 'gpt-wait-break-local'
        if ($LASTEXITCODE -ne 0) { throw 'Marketplace removal failed. Remove it manually before deleting its directory.' }
    }
}
foreach ($shortcutDirectory in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Startup'))) {
    $shortcutPath = Join-Path $shortcutDirectory 'GPT Wait Break.lnk'
    if ((Test-Path -LiteralPath $shortcutPath) -and $PSCmdlet.ShouldProcess($shortcutPath, 'Remove GPT Wait Break shortcut')) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}
if (Test-Path -LiteralPath $installRoot) {
    $receiptPath = Join-Path $installRoot 'install-receipt.json'
    if (-not (Test-Path -LiteralPath $receiptPath)) { throw 'Install receipt is missing; refusing recursive removal.' }
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.product -ne 'gpt-wait-break' -or $receipt.installRoot -ne $installRoot) { throw 'Install receipt does not match.' }
    if ($PSCmdlet.ShouldProcess($installRoot, 'Remove GPT Wait Break installed files')) { Remove-Item -LiteralPath $installRoot -Recurse -Force }
}
if ($RemoveData -and (Test-Path -LiteralPath $dataRoot) -and $PSCmdlet.ShouldProcess($dataRoot, 'Permanently remove settings and browser login/profile data')) {
    Remove-Item -LiteralPath $dataRoot -Recurse -Force
}
if (-not $RemoveData) { Write-Host "Settings and browser login/profile data preserved: $dataRoot" }
if (-not $RemovePlugin) { Write-Host 'If installed, remove the GPT Wait Break plugin and local marketplace in Codex separately.' }

[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$CodexPath, [switch]$ReviewHooks)

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
if (Test-Path -LiteralPath (Join-Path $pluginRoot 'plugin.json') -PathType Leaf) {
    throw 'An old portable plugin.json remains in this folder. Extract the new plugin ZIP into an empty folder and run Install.ps1 there.'
}
foreach ($requiredFile in @('.codex-plugin\plugin.json', 'hooks\hooks.json', 'scripts\hook.ps1', 'scripts\config.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $pluginRoot $requiredFile) -PathType Leaf)) {
        throw "Plugin file is missing: $requiredFile"
    }
}
if ($PSCmdlet.ShouldProcess('Codex plugins', 'Register GPT Wait Break marketplace and install the plugin')) {
    $marketplaceOutput = & $CodexPath plugin marketplace list --json
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect registered marketplaces. No registration was changed.' }
    $marketplaceList = ($marketplaceOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not $marketplaceList -or -not $marketplaceList.PSObject.Properties['marketplaces']) {
        throw 'Unexpected marketplace JSON. No registration was changed.'
    }
    $registered = @($marketplaceList.marketplaces | Where-Object {
        $_.PSObject.Properties['name'] -and $_.name -ceq 'gpt-wait-break-local'
    })
    if ($registered.Count -gt 1) { throw 'Ambiguous GPT Wait Break marketplace registration. No registration was changed.' }
    if ($registered.Count -eq 1) {
        if (-not $registered[0].PSObject.Properties['root'] -or $registered[0].root -isnot [string] -or
            -not [IO.Path]::IsPathRooted($registered[0].root)) {
            throw 'Unexpected GPT Wait Break marketplace root. No registration was changed.'
        }
        $registeredRoot = [IO.Path]::GetFullPath($registered[0].root).TrimEnd([char[]]'\/')
        if (-not [string]::Equals($registeredRoot, $pluginRoot.TrimEnd([char[]]'\/'), [StringComparison]::OrdinalIgnoreCase)) {
            Write-Host "Updating GPT Wait Break marketplace registration: $registeredRoot -> $pluginRoot"
            & $CodexPath plugin marketplace remove 'gpt-wait-break-local'
            if ($LASTEXITCODE -ne 0) { throw 'Previous marketplace registration could not be removed.' }
        }
    }
    & $CodexPath plugin marketplace add $pluginRoot
    if ($LASTEXITCODE -ne 0) { throw 'Marketplace registration failed.' }
    & $CodexPath plugin add 'gpt-wait-break@gpt-wait-break-local'
    if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed.' }
    Write-Host 'Installed. Restart the desktop app, enable the plugin, and review/trust its hooks.'
    if ($ReviewHooks) {
        $escapedCodexPath = $CodexPath.Replace("'", "''")
        $escapedPluginRoot = $pluginRoot.Replace("'", "''")
        $reviewCommand = @"
`$env:TERM = 'xterm-256color'
Write-Host '/hooks 在此命令窗口输入，不在 GPT 聊天框。'
& '$escapedCodexPath' --no-alt-screen --no-daemon -C '$escapedPluginRoot'
"@
        $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($reviewCommand))
        $powershellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        Start-Process -FilePath $powershellPath -ArgumentList @('-NoProfile', '-NoExit', '-EncodedCommand', $encodedCommand) -WindowStyle Normal
    }
}

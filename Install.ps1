[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$RegisterPlugin,
    [switch]$Start,
    [switch]$Startup
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) {
    throw 'GPT Wait Break requires Windows x64.'
}
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$installRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\GptWaitBreak'))
$sourceExe = Join-Path $sourceRoot 'bin\GptWaitBreak.exe'
if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) {
    throw 'bin\GptWaitBreak.exe is missing. Use the release ZIP or run scripts\build.ps1 first.'
}
$receiptPath = Join-Path $installRoot 'install-receipt.json'
if ((Test-Path -LiteralPath $installRoot) -and ((Get-Item -LiteralPath $installRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Install directory must not be a junction or symbolic link.'
}
if ((Test-Path -LiteralPath $installRoot) -and -not (Test-Path -LiteralPath $receiptPath)) {
    $existingItems = @(Get-ChildItem -LiteralPath $installRoot -Force)
    if ($existingItems.Count -gt 0) { throw "Install directory has unrelated files and no receipt: $installRoot" }
}
if (Test-Path -LiteralPath $receiptPath) {
    $receipt = Get-Content -LiteralPath $receiptPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($receipt.product -ne 'gpt-wait-break' -or $receipt.installRoot -ne $installRoot) {
        throw 'Existing install receipt does not match the expected product and path.'
    }
}
if ($RegisterPlugin -and -not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw 'codex CLI was not found. Install Codex CLI, or omit -RegisterPlugin and follow docs\INSTALL.md.'
}
if ($sourceRoot -ne $installRoot) {
    $runningApp = @(Get-Process -Name GptWaitBreak -ErrorAction SilentlyContinue)
    if ($runningApp.Count -gt 0) { throw 'Quit GPT Wait Break from its tray menu before installing or updating.' }
    if ($PSCmdlet.ShouldProcess($installRoot, 'Copy GPT Wait Break application and plugin files')) {
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
        $items = @('bin', 'plugin.json', '.codex-plugin', '.agents', 'marketplace.json', 'hooks', 'skills', 'docs', 'README.md', 'LICENSE', 'THIRD-PARTY-NOTICES.md', 'Install.ps1', 'Uninstall.ps1')
        foreach ($item in $items) {
            $sourcePath = Join-Path $sourceRoot $item
            if (-not (Test-Path -LiteralPath $sourcePath)) { throw "Release file is missing: $sourcePath" }
            Copy-Item -LiteralPath $sourcePath -Destination $installRoot -Recurse -Force
        }
        @{ product = 'gpt-wait-break'; installRoot = $installRoot; version = (Get-Content -LiteralPath (Join-Path $sourceRoot 'plugin.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version } |
            ConvertTo-Json | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    }
}
$installedExe = Join-Path $installRoot 'bin\GptWaitBreak.exe'
$startMenuPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'GPT Wait Break.lnk'
if ($PSCmdlet.ShouldProcess($startMenuPath, 'Create Start menu shortcut')) {
    $shortcutShell = New-Object -ComObject WScript.Shell
    $shortcut = $shortcutShell.CreateShortcut($startMenuPath)
    $shortcut.TargetPath = $installedExe
    $shortcut.Arguments = '--run'
    $shortcut.WorkingDirectory = Join-Path $installRoot 'bin'
    $shortcut.Save()
}
if ($Startup) {
    $startupPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'GPT Wait Break.lnk'
    if ($PSCmdlet.ShouldProcess($startupPath, 'Enable GPT Wait Break at user logon')) {
        $startupShell = New-Object -ComObject WScript.Shell
        $startupShortcut = $startupShell.CreateShortcut($startupPath)
        $startupShortcut.TargetPath = $installedExe
        $startupShortcut.Arguments = '--run'
        $startupShortcut.WorkingDirectory = Join-Path $installRoot 'bin'
        $startupShortcut.Save()
    }
}
if ($RegisterPlugin -and $PSCmdlet.ShouldProcess('Codex user plugin configuration', 'Register local marketplace and install plugin')) {
    & codex plugin marketplace add $installRoot
    if ($LASTEXITCODE -ne 0) { throw 'Marketplace registration failed. Application files remain installed; see docs\INSTALL.md.' }
    & codex plugin add 'gpt-wait-break@gpt-wait-break-local'
    if ($LASTEXITCODE -ne 0) { throw 'Plugin installation failed. See docs\INSTALL.md for desktop installation.' }
    Write-Host 'Restart the desktop app. Review and trust the current hooks in the official hook review flow.'
    Write-Host 'This installer does not trust hooks or change security settings.'
}
if ($Start -and $PSCmdlet.ShouldProcess($installedExe, 'Open GPT Wait Break settings')) {
    $companionProcess = Start-Process -FilePath $installedExe -ArgumentList '--run' -WindowStyle Hidden -PassThru
    try { $null = $companionProcess.WaitForInputIdle(2000) } catch { }
    Start-Process -FilePath $installedExe -ArgumentList '--configure' -WindowStyle Hidden
}
Write-Host "Install directory: $installRoot"
Write-Host 'Browser login/profile data is stored separately in %LOCALAPPDATA%\GptWaitBreak.'

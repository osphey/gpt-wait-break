[CmdletBinding()]
param(
    [string]$Url,
    [double]$DelaySeconds,
    [ValidateSet('auto', 'edge', 'chrome')][string]$Browser,
    [bool]$Enabled,
    [switch]$Show
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'state.ps1')
. (Join-Path $PSScriptRoot 'common.ps1')
$taskDataRoot = Get-WaitBreakDataRoot
$taskSettings = Get-WaitBreakSettings $taskDataRoot
$taskChanged = $false
foreach ($taskParameter in @('Url', 'DelaySeconds', 'Browser', 'Enabled')) {
    if ($PSBoundParameters.ContainsKey($taskParameter)) {
        $taskSettings.$taskParameter = $PSBoundParameters[$taskParameter]
        $taskChanged = $true
    }
}
if (-not (Test-WaitBreakUrl $taskSettings.url)) { throw 'Use an HTTP(S) URL without embedded credentials.' }
if ($taskSettings.delaySeconds -lt 0 -or $taskSettings.delaySeconds -gt 10 -or [double]::IsNaN($taskSettings.delaySeconds)) { throw 'Delay must be 0-10 seconds.' }
if ($taskChanged) {
    $null = New-Item -ItemType Directory -Path $taskDataRoot -Force
    $taskConfigPath = Join-Path $taskDataRoot 'settings.json'
    $taskTempPath = $taskConfigPath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [IO.File]::WriteAllText($taskTempPath, ($taskSettings | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $taskConfigPath) { [IO.File]::Replace($taskTempPath, $taskConfigPath, $null) }
        else { [IO.File]::Move($taskTempPath, $taskConfigPath) }
    } finally { if (Test-Path -LiteralPath $taskTempPath) { Remove-Item -LiteralPath $taskTempPath -Force } }
}
$taskSettings | ConvertTo-Json

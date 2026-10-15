[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskRoot = Join-Path $taskRepo ('.cache\plugin-tests\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $taskRoot -Force
. (Join-Path $taskRepo 'scripts\state.ps1')
. (Join-Path $taskRepo 'scripts\common.ps1')
. (Join-Path $PSScriptRoot 'state.tests.ps1')
$taskStateChecks = Invoke-WaitBreakStateTests -DataRoot (Join-Path $taskRoot 'state')
& (Join-Path $PSScriptRoot 'Browser.Tests.ps1')
$script:checks = 0
function Assert-Plugin([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }; $script:checks++
}
foreach ($badUrl in @('file:///C:/private', 'javascript:alert(1)', 'https://u:p@example.com', 'data:text/html,x', '')) {
    Assert-Plugin (-not (Test-WaitBreakUrl $badUrl)) 'Unsafe config URL accepted.'
}
Assert-Plugin (Test-WaitBreakUrl 'https://example.com/?q=hello&x=1') 'Valid custom URL rejected.'
$taskRaw = '{"session_id":"fixture-session","turn_id":"fixture-turn","prompt":"PRIVATE_PROMPT","last_assistant_message":"PRIVATE_REPLY","transcript_path":"PRIVATE_PATH"}'
$taskEvent = ConvertFrom-WaitBreakHook 'UserPromptSubmit' $taskRaw 123L
$taskSanitized = $taskEvent | ConvertTo-Json
Assert-Plugin ($taskEvent.emittedTicks -eq 123 -and $taskSanitized -notmatch 'PRIVATE_') 'Raw chat data leaked into event.'
foreach ($badJson in @('null','{}','{"session_id":12,"turn_id":"t"}','{"session_id":"s","turn_id":"bad|id"}')) {
    $rejected = $false; try { $null = ConvertFrom-WaitBreakHook 'Stop' $badJson 123L } catch { $rejected = $true }
    Assert-Plugin $rejected 'Invalid hook identifiers accepted.'
}
$taskOldData = $env:GPT_WAIT_BREAK_DATA_DIR
try {
    $env:GPT_WAIT_BREAK_DATA_DIR = Join-Path $taskRoot 'config'
    $taskConfig = & (Join-Path $taskRepo 'scripts\config.ps1') -Url 'https://example.com/' -DelaySeconds 1.5 -Browser edge -Enabled $false | ConvertFrom-Json
    Assert-Plugin ($taskConfig.url -eq 'https://example.com/' -and $taskConfig.delaySeconds -eq 1.5 -and -not $taskConfig.enabled) 'Configuration helper failed.'
    $taskLoaded = Get-WaitBreakSettings $env:GPT_WAIT_BREAK_DATA_DIR
    Assert-Plugin ($taskLoaded.url -eq $taskConfig.url -and -not $taskLoaded.enabled) 'Settings did not persist.'
} finally { $env:GPT_WAIT_BREAK_DATA_DIR = $taskOldData }

# Execute the real hook runner/manifest command against an inert browser adapter.
# This verifies startup/stop races without touching a browser or real ChatGPT window.
$taskFixtureRoot = Join-Path $taskRoot 'fixture'
$taskFixtureScripts = Join-Path $taskFixtureRoot 'scripts'
$null = New-Item -ItemType Directory -Path $taskFixtureScripts -Force
foreach ($name in @('hook.ps1','common.ps1','state.ps1')) { Copy-Item -LiteralPath (Join-Path $taskRepo ('scripts\' + $name)) -Destination $taskFixtureScripts }
$taskMock = @'
function Add-FixtureTrace([string]$Value) { [IO.File]::AppendAllText((Join-Path $env:GPT_WAIT_BREAK_DATA_DIR 'trace.txt'), ($Value + "`n")) }
function Get-WaitBreakChatTarget { return @{ Handle = 123L; ProcessId = 456 } }
function Test-WaitBreakChatTarget($Target) { return $null -ne $Target }
function Test-WaitBreakTargetForeground($Target) { return $true }
function Open-WaitBreakWebsite($Settings, $DataRoot, $PreviousBrowser) { Add-FixtureTrace 'open'; return @{ Handles = @(789L); ProcessIds = @(1234); Url = $Settings.url; Browser = 'edge'; Profile = (Join-Path $DataRoot 'BrowserProfile') } }
function Get-WaitBreakOwnedWindows($Browser) {
    $pause = Join-Path $env:GPT_WAIT_BREAK_DATA_DIR 'pause-query'
    if (Test-Path -LiteralPath $pause) {
        [IO.File]::WriteAllText((Join-Path $env:GPT_WAIT_BREAK_DATA_DIR 'query-ready'), 'ready')
        for ($index=0; $index -lt 200; $index++) {
            if (Test-Path -LiteralPath (Join-Path $env:GPT_WAIT_BREAK_DATA_DIR 'resume-query')) { break }
            Start-Sleep -Milliseconds 20
        }
    }
    return @{Handle=789L;ProcessId=1234}
}
function Show-WaitBreakWebsite($Browser, $Target, [object[]]$PreparedWindows) { Add-FixtureTrace 'show' }
function Hide-WaitBreakWebsite($Browser, $Target, [bool]$ReturnFocusBool, [object[]]$PreparedWindows) { if ($null -ne $Browser) { Add-FixtureTrace 'hide' } }
'@
[IO.File]::WriteAllText((Join-Path $taskFixtureScripts 'browser.ps1'), $taskMock, (New-Object Text.UTF8Encoding($true)))
$taskHooks = Get-Content -LiteralPath (Join-Path $taskRepo 'hooks\hooks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
function New-HookFixture([string]$Name, [double]$Delay) {
    $path = Join-Path $taskRoot $Name; $null = New-Item -ItemType Directory -Path $path -Force
    [IO.File]::WriteAllText((Join-Path $path 'settings.json'), (@{ url='https://example.com/'; delaySeconds=$Delay; browser='auto'; enabled=$true } | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    return $path
}
function Start-HookFixture([string]$EventName, [string]$DataRoot, [switch]$DeferInput) {
    $command = $taskHooks.hooks.$EventName[0].hooks[0].command
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName='cmd.exe'; $info.Arguments='/d /s /c "' + $command + '"'
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true
    $info.RedirectStandardInput=$true; $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $info.EnvironmentVariables['PLUGIN_ROOT']=$taskFixtureRoot
    $info.EnvironmentVariables['GPT_WAIT_BREAK_DATA_DIR']=$DataRoot
    $process=[Diagnostics.Process]::Start($info)
    if (-not $DeferInput) { $process.StandardInput.WriteLine($taskRaw); $process.StandardInput.Close() }
    return $process
}
function Wait-HookFixture($Process) {
    if (-not $Process.WaitForExit(12000)) { $Process.Kill(); throw 'Hook fixture timed out.' }
    $output=$Process.StandardOutput.ReadToEnd(); $errorText=$Process.StandardError.ReadToEnd()
    Assert-Plugin ($Process.ExitCode -eq 0 -and $output.Trim() -eq '{}' -and $errorText.Trim().Length -eq 0) ('Invalid hook output: '+$output+$errorText)
}
function Wait-FixturePhase([string]$DataRoot, [string]$Phase) {
    for($i=0;$i -lt 100;$i++) {
        $path=Join-Path $DataRoot 'state.json'
        if(Test-Path -LiteralPath $path) {
            $state=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
            if($null -ne $state.active -and $state.active.phase -eq $Phase){return}
        }
        Start-Sleep -Milliseconds 30
    }
    throw ('Hook fixture did not reach phase '+$Phase)
}
function Get-FixtureTrace([string]$DataRoot) {
    $path=Join-Path $DataRoot 'trace.txt'; if(Test-Path -LiteralPath $path){return @(Get-Content -LiteralPath $path -Encoding UTF8)}; return @()
}
$taskLong = New-HookFixture 'long' 0.1
Wait-HookFixture (Start-HookFixture 'UserPromptSubmit' $taskLong)
Assert-Plugin ((Get-FixtureTrace $taskLong) -join ',' -eq 'open,show') 'Long answer did not open the website.'
Wait-HookFixture (Start-HookFixture 'Stop' $taskLong)
Assert-Plugin ((Get-FixtureTrace $taskLong)[-1] -eq 'hide') 'Matching stop did not conceal website.'
Assert-Plugin ((Get-Content -LiteralPath (Join-Path $taskLong 'state.json') -Raw -Encoding UTF8) -notmatch 'PRIVATE_') 'Hook persisted raw chat content.'

$taskShort=New-HookFixture 'short' 2
$taskStart=Start-HookFixture 'UserPromptSubmit' $taskShort
Wait-FixturePhase $taskShort 'waiting'
Wait-HookFixture (Start-HookFixture 'Interrupt' $taskShort)
Wait-HookFixture $taskStart
Assert-Plugin (@(Get-FixtureTrace $taskShort).Count -eq 0) 'Interrupted short answer opened a website.'

$taskLate=New-HookFixture 'late' 0
$taskStart=Start-HookFixture 'UserPromptSubmit' $taskLate -DeferInput
Start-Sleep -Milliseconds 350
Wait-HookFixture (Start-HookFixture 'Interrupt' $taskLate)
$taskStart.StandardInput.WriteLine($taskRaw); $taskStart.StandardInput.Close()
Wait-HookFixture $taskStart
Assert-Plugin (@(Get-FixtureTrace $taskLate).Count -eq 0) 'Delayed start revived a completed turn.'

$taskContinue=New-HookFixture 'continue' 0
Wait-HookFixture (Start-HookFixture 'UserPromptSubmit' $taskContinue)
$taskStop=Start-HookFixture 'Stop' $taskContinue
Wait-FixturePhase $taskContinue 'finishing'
# Inject through the real state API after the stop is pending, so Windows process
# startup time cannot consume the entire debounce interval before the newer event.
$taskNewToken = Invoke-WaitBreakStateLock -DataRoot $taskContinue -Action {
    param($state)
    $oldEvent = @{ sessionId='fixture-session'; turnId='fixture-turn'; emittedTicks=($state.active.startedTicks + 1L) }
    Assert-Plugin ($null -eq (Start-WaitBreakTurn $state $oldEvent @{Handle=123L;ProcessId=456})) 'Pending stop accepted an older delayed start.'
    $newEvent = @{ sessionId='fixture-session'; turnId='fixture-turn'; emittedTicks=[DateTime]::UtcNow.Ticks }
    return Start-WaitBreakTurn $state $newEvent @{Handle=123L;ProcessId=456}
}
Assert-Plugin (-not [string]::IsNullOrWhiteSpace($taskNewToken)) 'New continuation was rejected.'
Wait-HookFixture $taskStop
Assert-Plugin ((Get-FixtureTrace $taskContinue) -notcontains 'hide') 'Old stop ended a newer continuation.'
Wait-HookFixture (Start-HookFixture 'Interrupt' $taskContinue)
Assert-Plugin ((Get-FixtureTrace $taskContinue)[-1] -eq 'hide') 'Continuation did not finish on interrupt.'

$taskHideRace=New-HookFixture 'hide-race' 0
Wait-HookFixture (Start-HookFixture 'UserPromptSubmit' $taskHideRace)
[IO.File]::WriteAllText((Join-Path $taskHideRace 'pause-query'), 'pause')
$taskStop=Start-HookFixture 'Stop' $taskHideRace
for ($index=0; $index -lt 150; $index++) {
    if (Test-Path -LiteralPath (Join-Path $taskHideRace 'query-ready')) { break }
    Start-Sleep -Milliseconds 20
}
Assert-Plugin (Test-Path -LiteralPath (Join-Path $taskHideRace 'query-ready')) 'Hide did not reach its prepared window query.'
$taskNewToken=Invoke-WaitBreakStateLock -DataRoot $taskHideRace -Action {
    param($state)
    Assert-Plugin ($null -eq $state.active) 'Completed stop has not released its turn.'
    Start-WaitBreakTurn $state @{sessionId='fixture-session';turnId='fixture-turn';emittedTicks=[DateTime]::UtcNow.Ticks} @{Handle=123L;ProcessId=456}
}
Assert-Plugin (-not [string]::IsNullOrWhiteSpace($taskNewToken)) 'New turn was not accepted during the old hide query.'
[IO.File]::WriteAllText((Join-Path $taskHideRace 'resume-query'), 'resume')
Wait-HookFixture $taskStop
Assert-Plugin ((Get-FixtureTrace $taskHideRace) -notcontains 'hide') 'An old terminal event hid a newer active turn.'
Wait-HookFixture (Start-HookFixture 'Interrupt' $taskHideRace)
Assert-Plugin ((Get-FixtureTrace $taskHideRace)[-1] -eq 'hide') 'Newest turn did not hide its website.'

Write-Host ('Plugin settings/protocol/integration: '+$script:checks+' checks passed.')
Write-Host ('Test artifacts: '+$taskRoot)

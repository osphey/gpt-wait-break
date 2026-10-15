param([string]$ModulePath = (Join-Path $PSScriptRoot '..\scripts\browser.ps1'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. $ModulePath
Initialize-WaitBreakNative
$checks = 0
function Assert-Browser([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}
$profile = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'wait break fixture', 'BrowserProfile')
foreach ($value in @('plain', 'with spaces', '汉字路径', 'C:\some path\', 'quote"inside', 'a\\"b')) {
    $line = 'fixture.exe ' + [WaitBreakPluginNative]::Quote($value)
    $arguments = [WaitBreakPluginNative]::Arguments($line)
    Assert-Browser ($arguments.Length -eq 2 -and $arguments[1] -ceq $value) ('Argument roundtrip failed: ' + $value)
}
$command = 'msedge.exe ' + [WaitBreakPluginNative]::Quote('--user-data-dir=' + $profile) + ' --new-window "https://example.com/"'
Assert-Browser (Test-WaitBreakProcessProfile $command $profile) 'Exact dedicated profile not recognized.'
Assert-Browser (Test-WaitBreakProcessProfile ('chrome.exe --user-data-dir ' + [WaitBreakPluginNative]::Quote($profile)) $profile) 'Separated profile argument not recognized.'
Assert-Browser (-not (Test-WaitBreakProcessProfile 'msedge.exe --new-window https://example.com/' $profile)) 'Regular browser was accepted.'
Assert-Browser (-not (Test-WaitBreakProcessProfile ($command + ' --user-data-dir=C:\wrong\BrowserProfile') $profile)) 'Duplicate profile arguments were accepted.'
Assert-Browser (-not (Test-WaitBreakProcessProfile ('msedge.exe --user-data-dir=' + [WaitBreakPluginNative]::Quote($profile + '-other')) $profile)) 'Profile prefix was accepted.'
Assert-Browser (-not (Test-WaitBreakProcessProfile 'msedge.exe --user-data-dir' $profile)) 'Missing profile value was accepted.'
Assert-Browser ((Get-WaitBreakWebUrl 'https://example.com/path?x=1&y=2') -ceq 'https://example.com/path?x=1&y=2') 'Valid website was changed.'
foreach ($badUrl in @('file:///C:/private', 'javascript:alert(1)', 'https://account:password@example.com/', 'https://', 'example.com', 'data:text/html,private')) {
    $rejected = $false
    try { Get-WaitBreakWebUrl $badUrl | Out-Null } catch { $rejected = $true }
    Assert-Browser $rejected ('Unsafe URL was accepted: ' + $badUrl)
}
Assert-Browser (-not (Test-WaitBreakChatTarget @{Handle=0;ProcessId=0})) 'Invalid target was accepted.'
Assert-Browser (-not (Test-WaitBreakTargetForeground @{Handle=0;ProcessId=0})) 'Invalid target foreground was accepted.'
$descriptor = @{Browser='edge';Url='https://example.com/';Profile=$profile}
$windows = @([pscustomobject]@{Handle=11L;ProcessId=101},[pscustomobject]@{Handle=22L;ProcessId=202})
$record = New-WaitBreakBrowserRecord $descriptor $windows @(22L)
Assert-Browser ($record.Handles.Count -eq 2 -and $record.Handles[0] -eq 22 -and $record.ProcessIds[0] -eq 202) 'Window/PID ordering was lost.'
Assert-Browser (@(Get-WaitBreakOwnedWindows @{Browser='edge';Profile='C:\unowned\BrowserProfile'}).Count -eq 0) 'Unowned profile was accepted.'
$launch = [WaitBreakPluginNative]::Arguments(('fixture.exe ' + (Get-WaitBreakBrowserArguments $profile 'https://example.com/?x=1&y=2')))
Assert-Browser ($launch.Length -eq 6 -and $launch[1] -ceq ('--user-data-dir=' + $profile) -and $launch[5] -ceq 'https://example.com/?x=1&y=2') 'Browser flags merged into a single argument.'
Assert-Browser (Test-WaitBreakBrowserReuse $record $descriptor $windows) 'Live previous window was not reused.'
Assert-Browser (-not (Test-WaitBreakBrowserReuse $record $descriptor @([pscustomobject]@{Handle=22L;ProcessId=999}))) 'Recycled HWND/PID was reused.'
Assert-Browser (-not (Test-WaitBreakBrowserReuse $record @{Browser='edge';Url='https://example.com/other';Profile=$profile} $windows)) 'Changed website was reused.'
# Prepared snapshots must avoid slow ownership queries even when stale or empty.
$script:preparedQueries = 0
function Get-WaitBreakOwnedWindows {
    param($Browser)
    $script:preparedQueries++
    throw 'Prepared window operations must not query CIM ownership inside the state mutex.'
}
Show-WaitBreakWebsite $record -PreparedWindows @()
Assert-Browser ($script:preparedQueries -eq 0) 'Empty prepared snapshot queried ownership.'
Show-WaitBreakWebsite $record -PreparedWindows @([pscustomobject]@{Handle=0L;ProcessId=0}, [pscustomobject]@{Handle='invalid';ProcessId=0})
Assert-Browser ($script:preparedQueries -eq 0) 'Stale prepared snapshot queried ownership.'
Hide-WaitBreakWebsite $record @{Handle=0L;ProcessId=0} $false -PreparedWindows @()
Assert-Browser ($script:preparedQueries -eq 0) 'Empty prepared hide snapshot queried ownership.'
Hide-WaitBreakWebsite $record @{Handle=0L;ProcessId=0} $false -PreparedWindows @([pscustomobject]@{Handle=0L;ProcessId=0}, [pscustomobject]@{Handle='invalid';ProcessId=0})
Assert-Browser ($script:preparedQueries -eq 0) 'Stale prepared hide snapshot queried ownership.'
Write-Output ('Browser module: ' + $checks + ' checks passed; no website or live chat window was accessed.')

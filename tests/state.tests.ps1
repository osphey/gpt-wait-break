param([switch]$HoldLock, [switch]$Increment, [string]$DataRoot, [string]$ReadyFile)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '..\scripts\state.ps1')

if ($HoldLock) {
    Invoke-WaitBreakStateLock $DataRoot {
        param($state)
        [IO.File]::WriteAllText($ReadyFile, 'ready')
        Start-Sleep -Milliseconds 1500
    }
    exit 0
}
if ($Increment) {
    for ($i = 0; $i -lt 20; $i++) {
        $done = $false
        for ($attempt = 0; $attempt -lt 12 -and -not $done; $attempt++) {
            try {
                Invoke-WaitBreakStateLock $DataRoot {
                    param($state)
                    $current = Get-WaitBreakTicks (Get-WaitBreakMember $state.ended 'counter|turn')
                    $state.ended['counter|turn'] = $current + 1
                }
                $done = $true
            } catch [TimeoutException] { Start-Sleep -Milliseconds 35 }
        }
        if (-not $done) { throw 'Fixture writer could not acquire lock.' }
    }
    exit 0
}

function Invoke-WaitBreakStateTests {
param([string]$DataRoot)
$script:WaitBreakStateChecks = 0
function Assert-WaitBreakState {
    param([bool]$Condition, [string]$Message)
    $script:WaitBreakStateChecks++
    if (-not $Condition) { throw ('State test failed: ' + $Message) }
}
function New-WaitBreakTestEvent {
    param([string]$Session, [string]$Turn, [long]$Ticks)
    return @{ sessionId = $Session; turnId = $Turn; emittedTicks = $Ticks }
}
$target = @{ Handle = [long]123; ProcessId = 42 }
$state = New-WaitBreakState
Assert-WaitBreakState (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent a 1 100)) 'initial start allowed'
Assert-WaitBreakState ($null -eq $state.active) 'preflight is read-only'
$token = Start-WaitBreakTurn $state (New-WaitBreakTestEvent a 1 100) $target
Assert-WaitBreakState ($token.Length -eq 32 -and $state.active.phase -eq 'waiting') 'start assigns token and waiting phase'
Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent a 1 100))) 'duplicate start rejected'
Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent a older 90))) 'older different-turn start rejected'
Assert-WaitBreakState ($null -eq (Start-WaitBreakTurn $state (New-WaitBreakTestEvent a older 90) $target)) 'rejected start cannot replace active'
Assert-WaitBreakState ($state.active.token -eq $token) 'rejection preserves token'
$state.active.phase = 'shown'
$state.active.stopToken = 'pending-stop'
$continued = Start-WaitBreakTurn $state (New-WaitBreakTestEvent a 1 200) $target
Assert-WaitBreakState ($continued -ne $token -and $state.active.startedTicks -eq 200) 'same-turn continuation replaces old worker token'
Assert-WaitBreakState ($null -eq $state.active.stopToken -and $state.active.phase -eq 'waiting') 'continuation cancels pending stop and restarts delay'
Assert-WaitBreakState ($null -eq (Complete-WaitBreakTurn $state (New-WaitBreakTestEvent a 1 150) stop)) 'old stop cannot finish continuation'
Assert-WaitBreakState ($null -ne $state.active) 'continuation remains active'
Assert-WaitBreakState ($null -eq (Complete-WaitBreakTurn $state (New-WaitBreakTestEvent b 1 250) interrupt)) 'other session cannot interrupt'
Assert-WaitBreakState ($state.ended['b|1'] -eq 250) 'unmatched terminal still recorded'
Assert-WaitBreakState ($null -eq (Complete-WaitBreakTurn $state (New-WaitBreakTestEvent a other 250) stop)) 'other turn cannot finish'
$state.active.phase = 'shown'
$result = Complete-WaitBreakTurn $state (New-WaitBreakTestEvent a 1 300) interrupt
Assert-WaitBreakState ($null -eq $state.active -and $result.target.Handle -eq 123 -and $result.phase -eq 'shown') 'interrupt clears matching active and retains target'
Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent a 1 200))) 'ended turn cannot revive from delayed start'
Assert-WaitBreakState ($null -eq (Complete-WaitBreakTurn $state (New-WaitBreakTestEvent a 1 300) interrupt)) 'duplicate terminal has no second action'
Assert-WaitBreakState (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent a 1 400)) 'newer continuation after terminal allowed'

$state = New-WaitBreakState
[void](Complete-WaitBreakTurn $state (New-WaitBreakTestEvent short reply 300) stop)
Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent short reply 100))) 'terminal arriving before delayed start wins'
[void](Start-WaitBreakTurn $state (New-WaitBreakTestEvent short next 400) $target)
$result = Complete-WaitBreakTurn $state (New-WaitBreakTestEvent short next 450) stop
Assert-WaitBreakState ($result.phase -eq 'waiting' -and $null -eq $state.active) 'short answer ends before website opening'
[void](Start-WaitBreakTurn $state (New-WaitBreakTestEvent a 2 500) $target)
$nextToken = Start-WaitBreakTurn $state (New-WaitBreakTestEvent b 3 600) $target
Assert-WaitBreakState ($state.active.sessionId -eq 'b' -and $nextToken -eq $state.active.token) 'caller-confirmed newer foreground start replaces active'
[void](Complete-WaitBreakTurn $state (New-WaitBreakTestEvent b '' 700) session-end)
Assert-WaitBreakState ($null -eq $state.active -and $state.endedSessions.b -eq 700) 'session end matches session without turn'
Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state (New-WaitBreakTestEvent b unseen 650))) 'session barrier rejects delayed unknown turn'
[void](Start-WaitBreakTurn $state (New-WaitBreakTestEvent b newer 800) $target)
Assert-WaitBreakState ($null -eq (Complete-WaitBreakTurn $state (New-WaitBreakTestEvent b '' 750) session-end)) 'old session end cannot close newer start'
Assert-WaitBreakState ($state.active.turnId -eq 'newer') 'newer start survives stale session end'
foreach ($bad in @(
    @{ sessionId = ''; turnId = 't'; emittedTicks = 900 },
    @{ sessionId = 's|collision'; turnId = 't'; emittedTicks = 900 },
    @{ sessionId = 's'; turnId = ''; emittedTicks = 900 },
    @{ sessionId = 's'; turnId = 't'; emittedTicks = -1 },
    @{ sessionId = 's'; turnId = 't'; emittedTicks = 'not-a-number' },
    @{ sessionId = "bad`nID"; turnId = 't'; emittedTicks = 900 }
)) { Assert-WaitBreakState (-not (Test-WaitBreakStartAllowed $state $bad)) 'invalid event rejected' }

$state = New-WaitBreakState
for ($i = 1; $i -le 130; $i++) { [void](Complete-WaitBreakTurn $state (New-WaitBreakTestEvent history $i $i) stop) }
Assert-WaitBreakState ($state.ended.Count -eq 128) 'history bound is 128'
Assert-WaitBreakState (-not $state.ended.ContainsKey('history|1') -and $state.ended.ContainsKey('history|130')) 'oldest history evicted individually'
[void](Complete-WaitBreakTurn $state (New-WaitBreakTestEvent history 130 1) stop)
Assert-WaitBreakState ($state.ended['history|130'] -eq 130) 'old duplicate never lowers terminal time'

$previousRoot = $env:GPT_WAIT_BREAK_DATA_DIR
if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $testRoot = Join-Path (Join-Path $PSScriptRoot '..\.cache') ('state-tests-' + [Guid]::NewGuid().ToString('N'))
} else { $testRoot = [IO.Path]::GetFullPath($DataRoot) }
[void][IO.Directory]::CreateDirectory($testRoot)
$testRoot = [IO.Path]::GetFullPath($testRoot)
try {
    $env:GPT_WAIT_BREAK_DATA_DIR = $testRoot
    Assert-WaitBreakState ((Get-WaitBreakDataRoot) -eq $testRoot) 'test data override is isolated'
    $storedToken = Invoke-WaitBreakStateLock $testRoot {
        param($saved)
        $saved.prompt = 'PRIVATE_PROMPT'
        $saved.answer = 'PRIVATE_ANSWER'
        $created = Start-WaitBreakTurn $saved (New-WaitBreakTestEvent saved turn 1000) $target
        $saved.active.prompt = 'PRIVATE_ACTIVE'
        $saved.active.target.prompt = 'PRIVATE_TARGET'
        $saved.browser = @{ Handles = @([long]456); ProcessIds = @(84); Browser = 'edge'; Url = 'https://example.com/'; Profile = (Join-Path $testRoot 'profile'); prompt = 'PRIVATE_BROWSER' }
        return $created
    }
    Assert-WaitBreakState ($storedToken.Length -eq 32) 'lock returns scriptblock output'
    $statePath = Join-Path $testRoot 'state.json'
    $original = [IO.File]::ReadAllText($statePath)
    Assert-WaitBreakState ($original -notmatch 'PRIVATE_|prompt|answer') 'persistence whitelists metadata and excludes content'
    $loaded = Invoke-WaitBreakStateLock $testRoot { param($saved) return $saved }
    Assert-WaitBreakState ($loaded.active.token -eq $storedToken -and $loaded.active.startedTicks -eq 1000) 'atomic state round trip'
    Assert-WaitBreakState ($loaded.ended -is [hashtable] -and $loaded.endedSessions -is [hashtable]) 'JSON histories restore mutable dictionaries'
    Assert-WaitBreakState ($loaded.browser.Handles.Count -eq 1 -and $loaded.browser.Handles[0] -eq 456) 'owned browser handle arrays preserved'
    Invoke-WaitBreakStateLock $testRoot { param($saved) $saved.browser.Handles = @(); $saved.browser.ProcessIds = @() }
    $emptyArrays = Invoke-WaitBreakStateLock $testRoot { param($saved) return $saved.browser }
    Assert-WaitBreakState ($emptyArrays.Handles.Count -eq 0 -and $emptyArrays.ProcessIds.Count -eq 0) 'empty browser arrays preserved'
    $original = [IO.File]::ReadAllText($statePath)
    $failed = $false
    try { Invoke-WaitBreakStateLock $testRoot { param($saved) $saved.active = $null; throw 'fixture failure' } }
    catch { $failed = $true }
    Assert-WaitBreakState ($failed -and [IO.File]::ReadAllText($statePath) -eq $original) 'failed transaction preserves previous state'
    Assert-WaitBreakState (@(Get-ChildItem -LiteralPath $testRoot -Filter 'state.*.tmp').Count -eq 0) 'no temporary files left after transaction'
    [IO.File]::WriteAllText($statePath, 'not-json')
    $failed = $false
    try { Invoke-WaitBreakStateLock $testRoot { param($saved) $saved.active = $null } } catch { $failed = $true }
    Assert-WaitBreakState ($failed -and [IO.File]::ReadAllText($statePath) -eq 'not-json') 'corrupt state fails without overwriting it'
    [IO.File]::WriteAllText($statePath, (' ' * 65537))
    $failed = $false
    try { Invoke-WaitBreakStateLock $testRoot { param($saved) } } catch { $failed = $true }
    Assert-WaitBreakState ($failed -and [IO.FileInfo]::new($statePath).Length -gt 65536) 'oversized state rejected before action'
    [IO.File]::WriteAllText($statePath, $original)

    $readyPath = Join-Path $testRoot 'holder.ready'
    $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -HoldLock -DataRoot "' + $testRoot + '" -ReadyFile "' + $readyPath + '"'
    $holder = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $wait = [Diagnostics.Stopwatch]::StartNew()
    while (-not [IO.File]::Exists($readyPath) -and $wait.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 25 }
    Assert-WaitBreakState ([IO.File]::Exists($readyPath)) 'separate-process lock holder started'
    $elapsed = [Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    try { Invoke-WaitBreakStateLock $testRoot { param($saved) } } catch [TimeoutException] { $timedOut = $true }
    $elapsed.Stop()
    Assert-WaitBreakState ($timedOut -and $elapsed.ElapsedMilliseconds -ge 450 -and $elapsed.ElapsedMilliseconds -lt 1200) 'lock contention has 500 ms wait budget'
    Assert-WaitBreakState ($holder.WaitForExit(5000) -and $holder.ExitCode -eq 0) 'holder releases lock normally'
    Invoke-WaitBreakStateLock $testRoot { param($saved) $saved.ended['counter|turn'] = [long]1000 }
    $workers = @()
    for ($i = 0; $i -lt 4; $i++) {
        $arguments = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Increment -DataRoot "' + $testRoot + '"'
        $worker = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $testRoot ('writer-' + $i + '.stderr.txt'))
        [void]$worker.Handle
        $workers += $worker
    }
    for ($i = 0; $i -lt 30; $i++) {
        [void]([IO.File]::ReadAllText($statePath) | ConvertFrom-Json)
        Start-Sleep -Milliseconds 15
    }
    Assert-WaitBreakState $true 'readers never observe partial JSON during atomic replacement'
    foreach ($worker in $workers) { Assert-WaitBreakState ($worker.WaitForExit(10000) -and $worker.ExitCode -eq 0) 'concurrent state writer succeeds' }
    $counter = Invoke-WaitBreakStateLock $testRoot { param($saved) return $saved.ended['counter|turn'] }
    Assert-WaitBreakState ($counter -eq 1080) 'cross-process updates are serialized without lost writes'
    Write-Host ('State: ' + $script:WaitBreakStateChecks + ' checks passed. Artifacts: ' + $testRoot)
}
finally { $env:GPT_WAIT_BREAK_DATA_DIR = $previousRoot }
return $script:WaitBreakStateChecks
}

if ($MyInvocation.InvocationName -ne '.') { Invoke-WaitBreakStateTests -DataRoot $DataRoot }

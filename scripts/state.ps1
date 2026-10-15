Set-StrictMode -Version Latest

function Get-WaitBreakDataRoot {
    $customRoot = $env:GPT_WAIT_BREAK_DATA_DIR
    if (-not [string]::IsNullOrWhiteSpace($customRoot)) { return [IO.Path]::GetFullPath($customRoot) }
    return [IO.Path]::Combine([Environment]::GetFolderPath('LocalApplicationData'), 'GptWaitBreakPlugin')
}

function Get-WaitBreakMember {
    param($Object, [string]$Name)
    if ($null -eq $Object) { return $null }
    if ($Object -is [Collections.IDictionary]) {
        if ($Object.Contains($Name)) { return $Object[$Name] }
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -ne $property) { return $property.Value }
    return $null
}

function Test-WaitBreakId {
    param($Value)
    return ($Value -is [string] -and -not [string]::IsNullOrWhiteSpace($Value) -and $Value.Length -le 200 -and $Value -notmatch '[\x00-\x1f\x7f|]')
}

function Get-WaitBreakTicks {
    param($Value)
    $ticks = [long]0
    if ($null -eq $Value -or -not [long]::TryParse([string]$Value, [Globalization.NumberStyles]::None,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$ticks) -or $ticks -le 0) { return [long]0 }
    return $ticks
}

function New-WaitBreakState {
    return @{ active = $null; browser = $null; ended = @{}; endedSessions = @{} }
}

function Get-WaitBreakHistoryKey {
    param([string]$SessionId, [string]$TurnId)
    return $SessionId + '|' + $TurnId
}

function Get-WaitBreakIdentity {
    param($Event, [switch]$SessionOnly)
    $session = Get-WaitBreakMember $Event 'sessionId'
    $turn = Get-WaitBreakMember $Event 'turnId'
    $ticks = Get-WaitBreakTicks (Get-WaitBreakMember $Event 'emittedTicks')
    if (-not (Test-WaitBreakId $session) -or $ticks -eq 0) { return $null }
    if (-not $SessionOnly -and -not (Test-WaitBreakId $turn)) { return $null }
    return @{ sessionId = $session; turnId = $turn; emittedTicks = $ticks }
}

function Test-WaitBreakStartAllowed {
    param([Parameter(Mandatory = $true)]$State, [Parameter(Mandatory = $true)]$Event)
    $identity = Get-WaitBreakIdentity $Event
    if ($null -eq $identity) { return $false }
    $key = Get-WaitBreakHistoryKey $identity.sessionId $identity.turnId
    $ended = Get-WaitBreakMember $State 'ended'
    $sessions = Get-WaitBreakMember $State 'endedSessions'
    if ((Get-WaitBreakTicks (Get-WaitBreakMember $ended $key)) -ge $identity.emittedTicks) { return $false }
    if ((Get-WaitBreakTicks (Get-WaitBreakMember $sessions $identity.sessionId)) -ge $identity.emittedTicks) { return $false }
    $active = Get-WaitBreakMember $State 'active'
    if ($null -ne $active -and $identity.emittedTicks -le (Get-WaitBreakTicks (Get-WaitBreakMember $active 'startedTicks'))) { return $false }
    return $true
}

function Start-WaitBreakTurn {
    param([Parameter(Mandatory = $true)]$State, [Parameter(Mandatory = $true)]$Event,
        [Parameter(Mandatory = $true)]$Target)
    if (-not (Test-WaitBreakStartAllowed $State $Event)) { return $null }
    $targetCopy = Copy-WaitBreakTarget $Target
    if ($null -eq $targetCopy) { throw [IO.InvalidDataException]::new('A start requires a bound target.') }
    $identity = Get-WaitBreakIdentity $Event
    # Every newer start gets fresh worker ownership, including same-turn continuations.
    # Existing delayed open/stop workers fail their token check; the browser stays reusable.
    $token = [Guid]::NewGuid().ToString('N')
    $State.active = @{ token = $token; sessionId = $identity.sessionId; turnId = $identity.turnId;
        startedTicks = $identity.emittedTicks; target = $targetCopy; phase = 'waiting'; stopToken = $null }
    return $token
}

function Add-WaitBreakTerminal {
    param($State, [string]$SessionId, [string]$TurnId, [long]$Ticks, [switch]$SessionOnly)
    if ($SessionOnly) { $dictionary = $State.endedSessions; $key = $SessionId }
    else { $dictionary = $State.ended; $key = Get-WaitBreakHistoryKey $SessionId $TurnId }
    if ((Get-WaitBreakTicks (Get-WaitBreakMember $dictionary $key)) -lt $Ticks) { $dictionary[$key] = $Ticks }
    Limit-WaitBreakHistory $State
}

function Complete-WaitBreakTurn {
    param([Parameter(Mandatory = $true)]$State, [Parameter(Mandatory = $true)]$Event,
        [Parameter(Mandatory = $true)][string]$Reason)
    $sessionOnly = $Reason -eq 'session-end' -or $Reason -eq 'SessionEnd'
    $identity = Get-WaitBreakIdentity $Event -SessionOnly:$sessionOnly
    if ($null -eq $identity) { return $null }
    Add-WaitBreakTerminal $State $identity.sessionId $identity.turnId $identity.emittedTicks -SessionOnly:$sessionOnly
    $active = Get-WaitBreakMember $State 'active'
    if ($null -eq $active -or $active.sessionId -ne $identity.sessionId -or
        (-not $sessionOnly -and $active.turnId -ne $identity.turnId) -or
        $identity.emittedTicks -lt $active.startedTicks) { return $null }
    $result = @{ target = $active.target; browser = $State.browser; token = $active.token; phase = $active.phase; reason = $Reason }
    $State.active = $null
    return $result
}

function Remove-WaitBreakOldestHistory {
    param($State)
    $candidates = @(
        foreach ($key in $State.ended.Keys) { [pscustomobject]@{ ticks = [long]$State.ended[$key]; kind = 'ended'; key = $key } }
        foreach ($key in $State.endedSessions.Keys) { [pscustomobject]@{ ticks = [long]$State.endedSessions[$key]; kind = 'endedSessions'; key = $key } }
    )
    if ($candidates.Count -eq 0) { return $false }
    $oldest = $candidates | Sort-Object ticks, kind, key | Select-Object -First 1
    [void]$State[$oldest.kind].Remove($oldest.key)
    return $true
}

function Limit-WaitBreakHistory {
    param($State)
    while (($State.ended.Count + $State.endedSessions.Count) -gt 128) { [void](Remove-WaitBreakOldestHistory $State) }
}

function Copy-WaitBreakTarget {
    param($Target)
    if ($null -eq $Target) { return $null }
    $handle = Get-WaitBreakTicks (Get-WaitBreakMember $Target 'Handle')
    $pidValue = Get-WaitBreakTicks (Get-WaitBreakMember $Target 'ProcessId')
    if ($handle -le 0 -or $pidValue -le 0 -or $pidValue -gt [int]::MaxValue) { throw [IO.InvalidDataException]::new('Invalid target binding.') }
    return @{ Handle = $handle; ProcessId = [int]$pidValue }
}

function Copy-WaitBreakBrowser {
    param($Browser)
    if ($null -eq $Browser) { return $null }
    $name = [string](Get-WaitBreakMember $Browser 'Browser')
    $url = [string](Get-WaitBreakMember $Browser 'Url')
    $profile = [string](Get-WaitBreakMember $Browser 'Profile')
    if ($name -notin @('edge', 'chrome') -or $url.Length -gt 4096 -or [string]::IsNullOrWhiteSpace($profile)) {
        throw [IO.InvalidDataException]::new('Invalid owned browser metadata.')
    }
    $handles = @(foreach ($value in @(Get-WaitBreakMember $Browser 'Handles')) {
        $number = Get-WaitBreakTicks $value
        if ($number -le 0) { throw [IO.InvalidDataException]::new('Invalid browser handle.') }
        $number
    })
    $pids = @(foreach ($value in @(Get-WaitBreakMember $Browser 'ProcessIds')) {
        $number = Get-WaitBreakTicks $value
        if ($number -le 0 -or $number -gt [int]::MaxValue) { throw [IO.InvalidDataException]::new('Invalid browser process.') }
        [int]$number
    })
    return @{ Handles = $handles; ProcessIds = $pids; Url = $url; Browser = $name; Profile = [IO.Path]::GetFullPath($profile) }
}

function Copy-WaitBreakHistory {
    param($History, [switch]$Sessions)
    $copy = @{}
    if ($null -eq $History) { return $copy }
    if ($History -is [Collections.IDictionary]) { $keys = @($History.Keys) }
    else { $keys = @($History.PSObject.Properties | ForEach-Object { $_.Name }) }
    foreach ($key in $keys) {
        if ($Sessions) { $valid = Test-WaitBreakId $key }
        else {
            $parts = @(([string]$key).Split('|'))
            $valid = $parts.Count -eq 2 -and (Test-WaitBreakId $parts[0]) -and (Test-WaitBreakId $parts[1])
        }
        $ticks = Get-WaitBreakTicks (Get-WaitBreakMember $History $key)
        if ($valid -and $ticks -gt 0) { $copy[[string]$key] = $ticks }
    }
    return $copy
}

function Copy-WaitBreakPersistedState {
    param($State)
    if ($null -eq $State) { throw [IO.InvalidDataException]::new('State must be a JSON object.') }
    $copy = New-WaitBreakState
    $copy.browser = Copy-WaitBreakBrowser (Get-WaitBreakMember $State 'browser')
    $copy.ended = Copy-WaitBreakHistory (Get-WaitBreakMember $State 'ended')
    $copy.endedSessions = Copy-WaitBreakHistory (Get-WaitBreakMember $State 'endedSessions') -Sessions
    $active = Get-WaitBreakMember $State 'active'
    if ($null -ne $active) {
        $identity = Get-WaitBreakIdentity @{ sessionId = (Get-WaitBreakMember $active 'sessionId');
            turnId = (Get-WaitBreakMember $active 'turnId'); emittedTicks = (Get-WaitBreakMember $active 'startedTicks') }
        $phase = [string](Get-WaitBreakMember $active 'phase')
        $token = [string](Get-WaitBreakMember $active 'token')
        $stopToken = Get-WaitBreakMember $active 'stopToken'
        if ($null -eq $identity -or $phase -notin @('waiting', 'opening', 'shown', 'finishing') -or
            [string]::IsNullOrWhiteSpace($token) -or $token.Length -gt 200 -or
            ($null -ne $stopToken -and ($stopToken -isnot [string] -or $stopToken.Length -gt 200))) {
            throw [IO.InvalidDataException]::new('Invalid active turn state.')
        }
        $target = Copy-WaitBreakTarget (Get-WaitBreakMember $active 'target')
        if ($null -eq $target) { throw [IO.InvalidDataException]::new('Active turn has no bound target.') }
        $copy.active = @{ token = $token; sessionId = $identity.sessionId; turnId = $identity.turnId;
            startedTicks = $identity.emittedTicks; target = $target; phase = $phase; stopToken = $stopToken }
    }
    Limit-WaitBreakHistory $copy
    return $copy
}

function Get-WaitBreakMutexName {
    param([string]$DataRoot)
    $user = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $canonical = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\', '/').ToUpperInvariant()
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $digest = $hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($user + '|' + $canonical)) }
    finally { $hash.Dispose() }
    return 'Local\GptWaitBreakPlugin-' + ([BitConverter]::ToString($digest).Replace('-', '').Substring(0, 32))
}

function Invoke-WaitBreakStateLock {
    param([Parameter(Mandatory = $true)][string]$DataRoot, [Parameter(Mandatory = $true)][scriptblock]$Action)
    $resolvedRoot = [IO.Path]::GetFullPath($DataRoot)
    $mutex = [Threading.Mutex]::new($false, (Get-WaitBreakMutexName $resolvedRoot))
    $acquired = $false
    $temporary = $null
    try {
        try { $acquired = $mutex.WaitOne(500) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw [TimeoutException]::new('Wait Break state is busy.') }
        [void][IO.Directory]::CreateDirectory($resolvedRoot)
        $path = [IO.Path]::Combine($resolvedRoot, 'state.json')
        $state = New-WaitBreakState
        if ([IO.File]::Exists($path)) {
            if ([IO.FileInfo]::new($path).Length -gt 65536) { throw [IO.InvalidDataException]::new('State exceeds 64 KB.') }
            $raw = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) | ConvertFrom-Json
            if ($null -eq $raw -or $raw -is [Array] -or $raw -is [string] -or $raw -is [ValueType]) { throw [IO.InvalidDataException]::new('State must be a JSON object.') }
            $state = Copy-WaitBreakPersistedState $raw
        }
        $output = @(& $Action $state)
        # Persist only the explicit metadata schema, even if a caller accidentally adds a prompt.
        $saved = Copy-WaitBreakPersistedState $state
        $encoding = [Text.UTF8Encoding]::new($false)
        $json = $saved | ConvertTo-Json -Depth 12 -Compress
        while ($encoding.GetByteCount($json) -gt 65536 -and ($saved.ended.Count + $saved.endedSessions.Count) -gt 0) {
            [void](Remove-WaitBreakOldestHistory $saved)
            $json = $saved | ConvertTo-Json -Depth 12 -Compress
        }
        if ($encoding.GetByteCount($json) -gt 65536) { throw [IO.InvalidDataException]::new('State exceeds 64 KB.') }
        $temporary = [IO.Path]::Combine($resolvedRoot, ('state.' + [Guid]::NewGuid().ToString('N') + '.tmp'))
        [IO.File]::WriteAllText($temporary, $json, $encoding)
        if ([IO.File]::Exists($path)) { [IO.File]::Replace($temporary, $path, [System.Management.Automation.Language.NullString]::Value) }
        else { [IO.File]::Move($temporary, $path) }
        $temporary = $null
        return $output
    }
    finally {
        if ($null -ne $temporary -and [IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}

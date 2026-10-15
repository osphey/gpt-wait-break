[CmdletBinding()]
param([Parameter(Mandatory = $true)][ValidateSet('UserPromptSubmit', 'Stop', 'Interrupt', 'PermissionRequest', 'SessionEnd')][string]$EventName)
$taskEmittedTicks = [DateTime]::UtcNow.Ticks
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'state.ps1')
    . (Join-Path $PSScriptRoot 'common.ps1')
    . (Join-Path $PSScriptRoot 'browser.ps1')
    function Hide-WaitBreakIdleBrowser($Browser, $Target, [string]$DataRoot) {
        if ($null -eq $Browser) { return }
        $prepared = @(Get-WaitBreakOwnedWindows $Browser)
        $null = Invoke-WaitBreakStateLock -DataRoot $DataRoot -Action {
            param($state)
            # A newer turn owns the window once it is active. Keep slow queries outside
            # the lock, but order native hide/show operations with turn transitions.
            if ($null -eq $state.active) { $null = Hide-WaitBreakWebsite $Browser $Target $true -PreparedWindows $prepared }
        }
    }
    $taskEvent = ConvertFrom-WaitBreakHook $EventName (Read-WaitBreakHookInput) $taskEmittedTicks
    $taskDataRoot = Get-WaitBreakDataRoot
    if ($EventName -eq 'UserPromptSubmit') {
        $taskSettings = Get-WaitBreakSettings $taskDataRoot
        if ($taskSettings.enabled) {
            $taskTarget = Get-WaitBreakChatTarget
            if ($null -eq $taskTarget) {
                $taskTarget = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
                    param($state)
                    if ($null -ne $state.active -and $state.active.sessionId -eq $taskEvent.sessionId -and (Test-WaitBreakChatTarget $state.active.target)) { return $state.active.target }
                    return $null
                }
            }
            if ($null -ne $taskTarget) {
                $taskToken = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
                    param($state)
                    Start-WaitBreakTurn $state $taskEvent $taskTarget
                }
                if ($taskToken) {
                    Start-Sleep -Milliseconds ([int]($taskSettings.delaySeconds * 1000))
                    $taskReady = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
                        param($state)
                        if ($null -ne $state.active -and $state.active.token -eq $taskToken -and $state.active.phase -eq 'waiting') {
                            if ((Test-WaitBreakChatTarget $taskTarget) -and (Test-WaitBreakTargetForeground $taskTarget)) {
                                $state.active.phase = 'opening'
                                return @{ previousBrowser = $state.browser }
                            }
                        }
                        return $null
                    }
                    if ($null -ne $taskReady) {
                        $taskBrowser = Open-WaitBreakWebsite $taskSettings $taskDataRoot $taskReady.previousBrowser
                        if ($null -ne $taskBrowser) {
                            $taskShowWindows = @(Get-WaitBreakOwnedWindows $taskBrowser)
                            $taskVisible = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
                                param($state)
                                # Keep the reusable browser record even if a terminal event won.
                                if ($null -eq $state.active -or $state.active.token -eq $taskToken) { $state.browser = $taskBrowser }
                                if ($null -ne $state.active -and $state.active.token -eq $taskToken -and $state.active.phase -eq 'opening' -and (Test-WaitBreakChatTarget $taskTarget)) {
                                    $state.active.phase = 'shown'
                                    $null = Show-WaitBreakWebsite $taskBrowser $taskTarget -PreparedWindows $taskShowWindows
                                    return $true
                                }
                                return $false
                            }
                            if (-not $taskVisible) { $null = Hide-WaitBreakIdleBrowser $taskBrowser $taskTarget $taskDataRoot }
                        }
                    }
                }
            }
        }
    } elseif ($EventName -eq 'Stop') {
        $taskPendingStop = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
            param($state)
            if ($null -ne $state.active -and $state.active.sessionId -eq $taskEvent.sessionId -and $state.active.turnId -eq $taskEvent.turnId -and $taskEvent.emittedTicks -gt $state.active.startedTicks) {
                Add-WaitBreakTerminal -State $state -SessionId $taskEvent.sessionId -TurnId $taskEvent.turnId -Ticks $taskEvent.emittedTicks
                if ($state.active.phase -eq 'finishing') { return $null }
                $state.active.phase = 'finishing'
                $state.active.stopToken = [Guid]::NewGuid().ToString('N')
                return $state.active.stopToken
            }
            $null = Complete-WaitBreakTurn $state $taskEvent 'stop'
            return $null
        }
        if ($taskPendingStop) {
            Start-Sleep -Milliseconds 700
            $taskFinish = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
                param($state)
                if ($null -ne $state.active -and $state.active.stopToken -eq $taskPendingStop -and $state.active.phase -eq 'finishing') {
                    return Complete-WaitBreakTurn $state $taskEvent 'stop'
                }
                return $null
            }
            if ($taskFinish) { $null = Hide-WaitBreakIdleBrowser $taskFinish.browser $taskFinish.target $taskDataRoot }
        }
    } else {
        $taskReason = if ($EventName -eq 'SessionEnd') { 'session-end' } else { $EventName.ToLowerInvariant() }
        $taskFinish = Invoke-WaitBreakStateLock -DataRoot $taskDataRoot -Action {
            param($state)
            Complete-WaitBreakTurn $state $taskEvent $taskReason
        }
        if ($taskFinish) { $null = Hide-WaitBreakIdleBrowser $taskFinish.browser $taskFinish.target $taskDataRoot }
    }
} catch {
    # Hooks observe the agent lifecycle: never reject, approve, continue, or log prompt bodies.
}
[Console]::Out.WriteLine('{}')
exit 0

Set-StrictMode -Version Latest

function Test-WaitBreakUrl {
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url) -or $Url.Length -gt 4096) { return $false }
    $parsed = $null
    return [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$parsed) -and
        $parsed.Scheme -in @('https', 'http') -and $parsed.Host.Length -gt 0 -and $parsed.UserInfo.Length -eq 0
}

function Get-WaitBreakSettings {
    param([string]$DataRoot)
    $settings = [pscustomobject]@{ url = 'https://www.bilibili.com/'; delaySeconds = 2.0; browser = 'auto'; enabled = $true }
    $path = Join-Path $DataRoot 'settings.json'
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        if ((Get-Item -LiteralPath $path).Length -gt 16384) { throw 'Settings file exceeds 16 KB.' }
        $stored = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($null -eq $stored) { throw 'Settings must be an object.' }
        foreach ($key in @('url', 'delaySeconds', 'browser', 'enabled')) {
            if ($null -ne $stored.PSObject.Properties[$key]) { $settings.$key = $stored.$key }
        }
    }
    if (-not (Test-WaitBreakUrl $settings.url)) { throw 'Use a valid HTTP(S) URL without embedded credentials.' }
    if ($settings.browser -notin @('auto', 'edge', 'chrome')) { throw 'Browser must be auto, edge, or chrome.' }
    if ($settings.enabled -isnot [bool]) { throw 'enabled must be a JSON boolean.' }
    $delay = 0.0
    if (-not [double]::TryParse([string]$settings.delaySeconds, [ref]$delay) -or $delay -lt 0 -or $delay -gt 10 -or [double]::IsNaN($delay)) { throw 'Delay must be 0-10 seconds.' }
    $settings.delaySeconds = $delay
    return $settings
}

function ConvertFrom-WaitBreakHook {
    param([string]$EventName, [string]$Json, [long]$EmittedTicks)
    if ($Json.Length -gt 2097152) { throw 'Hook input exceeds 2 MB.' }
    $raw = $Json | ConvertFrom-Json
    if ($null -eq $raw) { throw 'Missing hook input.' }
    $session = ''; $turn = ''
    if ($null -ne $raw.PSObject.Properties['session_id']) { $session = $raw.session_id }
    if ($null -ne $raw.PSObject.Properties['turn_id']) { $turn = $raw.turn_id }
    if ($session -isnot [string] -or [string]::IsNullOrWhiteSpace($session) -or $session.Length -gt 200 -or $session -match '[\r\n\x00|]') { throw 'Invalid session identifier.' }
    if ($turn -isnot [string] -or $turn.Length -gt 200 -or $turn -match '[\r\n\x00|]' -or ($EventName -ne 'SessionEnd' -and [string]::IsNullOrWhiteSpace($turn))) { throw 'Invalid turn identifier.' }
    # Do not retain prompts, assistant responses, transcript paths or tool arguments.
    return @{ sessionId = $session; turnId = $turn; emittedTicks = $EmittedTicks }
}

function Read-WaitBreakHookInput {
    $reader = New-Object IO.StreamReader -ArgumentList @([Console]::OpenStandardInput(), (New-Object Text.UTF8Encoding($false)), $true)
    try {
        $builder = New-Object Text.StringBuilder
        $buffer = New-Object char[] 4096
        while (($read = $reader.Read($buffer, 0, $buffer.Length)) -gt 0) {
            if ($builder.Length + $read -gt 2097152) { throw 'Hook input exceeds 2 MB.' }
            $null = $builder.Append($buffer, 0, $read)
        }
        return $builder.ToString()
    } finally { $reader.Dispose() }
}

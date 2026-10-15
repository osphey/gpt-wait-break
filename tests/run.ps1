[CmdletBinding()]
param([switch]$SkipBuild, [switch]$Browser)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$taskRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $taskRepo 'scripts\build.ps1') }
$taskFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$taskCompiler = Join-Path $taskFramework 'csc.exe'
$taskBin = Join-Path $taskRepo 'bin'
$taskTestDirectory = Join-Path $taskRepo ('.cache\tests\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskTestDirectory -Force | Out-Null
$taskPreviousDataDir = $env:GPT_WAIT_BREAK_DATA_DIR
try {
    $env:GPT_WAIT_BREAK_DATA_DIR = Join-Path $taskTestDirectory 'data'
    $taskRefs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','WPF\UIAutomationClient.dll','WPF\UIAutomationTypes.dll','WPF\WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path $taskFramework $_) }
    $taskRefs += '/reference:' + (Join-Path $taskBin 'Microsoft.Web.WebView2.Core.dll')
    $taskRefs += '/reference:' + (Join-Path $taskBin 'Microsoft.Web.WebView2.WinForms.dll')
    $taskSources = @(Get-ChildItem -LiteralPath (Join-Path $taskRepo 'src') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
    $taskSources += @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
    & $taskCompiler /nologo /target:exe /platform:x64 /langversion:5 /main:TestRunner ('/out:' + (Join-Path $taskBin 'Tests.exe')) @taskRefs @taskSources
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & (Join-Path $taskBin 'Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'State/configuration/privacy tests failed.' }
    $taskHooks = Get-Content -LiteralPath (Join-Path $taskRepo 'hooks\hooks.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($taskEvent in @('UserPromptSubmit','Stop','Interrupt','PermissionRequest','SessionEnd')) {
        $taskHandler = $taskHooks.hooks.$taskEvent[0].hooks[0]
        if ($taskHandler.timeout -gt 3 -or $taskHandler.command -notmatch ('--hook ' + $taskEvent)) { throw ('Invalid hook: ' + $taskEvent) }
    }
    # Execute the shipping PowerShell wrapper with an inert stop event. It must emit exactly {}.
    $taskOldPluginRoot = $env:PLUGIN_ROOT
    try {
        $env:PLUGIN_ROOT = $taskRepo
        $taskHookCapture = Join-Path $taskTestDirectory 'hook-captured.json'
        $taskFixture = Start-Process -FilePath (Join-Path $taskBin 'Tests.exe') -ArgumentList ('--ipc-fixture "' + $taskHookCapture + '"') -WindowStyle Hidden -PassThru
        Start-Sleep -Milliseconds 200
        $taskCommand = $taskHooks.hooks.Stop[0].hooks[0].command
        $taskRawHook = '{"session_id":"wrapper-fixture","turn_id":"wrapper-fixture","last_assistant_message":"PRIVATE_FIXTURE"}'
        $taskStart = New-Object Diagnostics.ProcessStartInfo
        $taskStart.FileName = 'cmd.exe'
        $taskStart.Arguments = '/d /s /c "' + $taskCommand + '"'
        $taskStart.UseShellExecute = $false
        $taskStart.CreateNoWindow = $true
        $taskStart.RedirectStandardInput = $true
        $taskStart.RedirectStandardOutput = $true
        $taskStart.RedirectStandardError = $true
        $taskProcess = [Diagnostics.Process]::Start($taskStart)
        $taskProcess.StandardInput.WriteLine($taskRawHook)
        $taskProcess.StandardInput.Close()
        $taskOutput = $taskProcess.StandardOutput.ReadToEnd()
        $taskError = $taskProcess.StandardError.ReadToEnd()
        $taskProcess.WaitForExit()
        if ($taskProcess.ExitCode -ne 0 -or $taskOutput.Trim() -ne '{}' -or $taskError.Trim().Length -ne 0) { throw ('Shipping hook wrapper failed: ' + $taskOutput + $taskError) }
        if (-not $taskFixture.WaitForExit(12000)) { $taskFixture.Kill(); throw 'Hook IPC fixture timed out.' }
        if ($taskFixture.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $taskHookCapture)) { throw 'Shipping wrapper did not forward its event.' }
        $taskCapturedHook = Get-Content -LiteralPath $taskHookCapture -Raw -Encoding UTF8
        if ($taskCapturedHook.Contains('PRIVATE_FIXTURE') -or -not $taskCapturedHook.Contains('wrapper-fixture')) { throw 'Hook forwarding privacy assertion failed.' }
        Write-Host 'Hook wrapper: clean JSON output; private payload excluded.'
    } finally { $env:PLUGIN_ROOT = $taskOldPluginRoot }
    if ($Browser) {
        foreach ($taskStage in @('initial','reopen')) {
            $taskReport = Join-Path $taskTestDirectory ($taskStage + '.json')
            $taskArgs = '--self-test-browser "' + $taskReport + '"'
            if ($taskStage -eq 'reopen') { $taskArgs += ' --read-cookie' }
            $taskBrowser = Start-Process -FilePath (Join-Path $taskBin 'GptWaitBreak.exe') -ArgumentList $taskArgs -WindowStyle Hidden -PassThru
            if (-not $taskBrowser.WaitForExit(30000)) { $taskBrowser.Kill(); throw 'Browser integration test timed out.' }
            if (-not (Test-Path -LiteralPath $taskReport)) { throw 'Browser integration test did not write its report.' }
            $taskResult = Get-Content -LiteralPath $taskReport -Raw -Encoding UTF8 | ConvertFrom-Json
            if (-not $taskResult.passed -or $taskBrowser.ExitCode -ne 0) { throw ('Browser integration failed: ' + (Get-Content -LiteralPath $taskReport -Raw -Encoding UTF8)) }
            Write-Host ('Browser ' + $taskStage + ': ' + (Get-Content -LiteralPath $taskReport -Raw -Encoding UTF8))
            Start-Sleep -Milliseconds 1000
        }
    }
    Write-Host ('Test artifacts: ' + $taskTestDirectory)
} finally { $env:GPT_WAIT_BREAK_DATA_DIR = $taskPreviousDataDir }

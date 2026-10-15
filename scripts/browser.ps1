# Windows PowerShell 5.1. Dot-source this module from the short-lived hook handler.
# Browser pages, window titles, chat content, cookies and credentials are never read.

function Initialize-WaitBreakNative {
    if ('WaitBreakPluginNative' -as [type]) { return }
    $source = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
public sealed class WaitBreakWindow {
    public long Handle;
    public int ProcessId;
}
public static class WaitBreakPluginNative {
    private delegate bool EnumCallback(IntPtr hwnd, IntPtr state);
    [StructLayout(LayoutKind.Sequential)] private struct FLASHWINFO {
        public uint cbSize; public IntPtr hwnd; public uint flags; public uint count; public uint timeout;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr state);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO info);
    [DllImport("shell32.dll", SetLastError=true)] private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string commandLine, out int count);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static long Foreground() { return GetForegroundWindow().ToInt64(); }
    public static int Pid(long handle) {
        IntPtr hwnd = new IntPtr(handle); uint pid;
        if (handle == 0 || !IsWindow(hwnd)) return 0;
        GetWindowThreadProcessId(hwnd, out pid);
        return pid > Int32.MaxValue ? 0 : (int)pid;
    }
    public static bool Valid(long handle, int pid) { return pid > 0 && Pid(handle) == pid; }
    public static bool Chat(long handle, int pid) {
        IntPtr hwnd = new IntPtr(handle);
        if (!Valid(handle, pid) || !IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd || GetWindow(hwnd, 4) != IntPtr.Zero) return false;
        try {
            using (Process process = Process.GetProcessById(pid)) {
                return String.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase);
            }
        } catch { return false; }
    }
    public static WaitBreakWindow[] Windows(int[] processIds) {
        HashSet<int> allowed = new HashSet<int>(processIds ?? new int[0]);
        List<WaitBreakWindow> result = new List<WaitBreakWindow>();
        EnumWindows(delegate(IntPtr hwnd, IntPtr state) {
            long handle = hwnd.ToInt64(); int pid = Pid(handle);
            if (allowed.Contains(pid) && GetAncestor(hwnd, 2) == hwnd && (IsWindowVisible(hwnd) || IsIconic(hwnd)))
                result.Add(new WaitBreakWindow { Handle=handle, ProcessId=pid });
            return true;
        }, IntPtr.Zero);
        return result.ToArray();
    }
    public static void Minimize(long handle, int pid) { if (Valid(handle,pid)) ShowWindowAsync(new IntPtr(handle), 6); }
    public static void Restore(long handle, int pid) {
        if (Valid(handle,pid)) ShowWindowAsync(new IntPtr(handle), IsIconic(new IntPtr(handle)) ? 9 : 4);
    }
    public static bool Focus(long handle, int pid) {
        if (!Valid(handle,pid)) return false;
        if (Foreground() == handle) return true;
        Restore(handle,pid);
        return Valid(handle,pid) && SetForegroundWindow(new IntPtr(handle));
    }
    public static void Flash(long handle, int pid) {
        if (!Valid(handle,pid)) return;
        FLASHWINFO info = new FLASHWINFO { cbSize=(uint)Marshal.SizeOf(typeof(FLASHWINFO)), hwnd=new IntPtr(handle), flags=2, count=3, timeout=0 };
        FlashWindowEx(ref info);
    }
    public static string[] Arguments(string commandLine) {
        if (String.IsNullOrWhiteSpace(commandLine)) return new string[0];
        int count; IntPtr memory = CommandLineToArgvW(commandLine, out count);
        if (memory == IntPtr.Zero) return new string[0];
        try {
            if (count < 0 || count > 4096) return new string[0];
            string[] result = new string[count];
            for (int i=0; i<count; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i*IntPtr.Size));
            return result;
        } finally { LocalFree(memory); }
    }
    public static string Quote(string argument) {
        StringBuilder result = new StringBuilder("\""); int slashes=0;
        foreach (char value in argument ?? String.Empty) {
            if (value == '\\') { slashes++; continue; }
            if (value == '"') { result.Append('\\',slashes*2+1); result.Append('"'); }
            else { result.Append('\\',slashes); result.Append(value); }
            slashes=0;
        }
        result.Append('\\',slashes*2); result.Append('"'); return result.ToString();
    }
}
'@
    Add-Type -TypeDefinition $source -Language CSharp -ErrorAction Stop
}

function Get-WaitBreakProperty {
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

function Get-WaitBreakWebUrl {
    param([string]$Url)
    $uri = $null
    if ([string]::IsNullOrWhiteSpace($Url) -or $Url.Length -gt 4096 -or
        -not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http','https') -or [string]::IsNullOrEmpty($uri.Host) -or
        -not [string]::IsNullOrEmpty($uri.UserInfo)) { throw '请设置有效的 HTTP(S) 网站网址，网址不能包含账号密码。' }
    return $uri.AbsoluteUri
}

function Get-WaitBreakCanonicalProfile {
    param([string]$Profile)
    if ([string]::IsNullOrWhiteSpace($Profile) -or -not [IO.Path]::IsPathRooted($Profile)) { return $null }
    try {
        $full = [IO.Path]::GetFullPath($Profile).TrimEnd('\','/')
        if ([IO.Path]::GetFileName($full) -cne 'BrowserProfile') { return $null }
        return $full
    } catch { return $null }
}

function Test-WaitBreakOwnedProfile {
    param([string]$Profile)
    $profilePath = Get-WaitBreakCanonicalProfile $Profile
    if ($null -eq $profilePath) { return $false }
    $marker = Join-Path $profilePath '.gpt-wait-break-profile'
    try {
        if (-not [IO.File]::Exists($marker) -or (Get-Item -LiteralPath $marker -ErrorAction Stop).Length -gt 100) { return $false }
        return [IO.File]::ReadAllText($marker) -ceq 'gpt-wait-break/plugin-profile/v2'
    } catch { return $false }
}

function Get-WaitBreakChatTarget {
    Initialize-WaitBreakNative
    $handle = [WaitBreakPluginNative]::Foreground()
    $processId = [WaitBreakPluginNative]::Pid($handle)
    if ([WaitBreakPluginNative]::Chat($handle, $processId)) { return @{ Handle=[long]$handle; ProcessId=[int]$processId } }
    return $null
}

function Test-WaitBreakChatTarget {
    param($Target)
    Initialize-WaitBreakNative
    try { return [WaitBreakPluginNative]::Chat([long](Get-WaitBreakProperty $Target 'Handle'), [int](Get-WaitBreakProperty $Target 'ProcessId')) }
    catch { return $false }
}

function Test-WaitBreakTargetForeground {
    param($Target)
    if (-not (Test-WaitBreakChatTarget $Target)) { return $false }
    return [WaitBreakPluginNative]::Foreground() -eq [long](Get-WaitBreakProperty $Target 'Handle')
}

function Get-WaitBreakBrowserExecutable {
    param([ValidateSet('auto','edge','chrome')][string]$Browser = 'auto')
    $choices = if ($Browser -eq 'auto') { @('edge','chrome') } else { @($Browser) }
    foreach ($choice in $choices) {
        $fileName = if ($choice -eq 'edge') { 'msedge.exe' } else { 'chrome.exe' }
        $relative = if ($choice -eq 'edge') { 'Microsoft\Edge\Application\msedge.exe' } else { 'Google\Chrome\Application\chrome.exe' }
        $candidates = New-Object 'Collections.Generic.List[string]'
        foreach ($key in @('Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\App Paths\', 'Registry::HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\App Paths\', 'Registry::HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\')) {
            try {
                $value = (Get-Item -LiteralPath ($key + $fileName) -ErrorAction Stop).GetValue('')
                if ($value) { $candidates.Add(([string]$value).Trim('"')) }
            } catch { }
        }
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:LOCALAPPDATA)) {
            if (-not [string]::IsNullOrWhiteSpace($base)) { $candidates.Add((Join-Path $base $relative)) }
        }
        foreach ($candidate in $candidates) {
            if ([IO.File]::Exists($candidate) -and [IO.Path]::GetFileName($candidate) -ieq $fileName) {
                return @{ Browser=$choice; Path=[IO.Path]::GetFullPath($candidate) }
            }
        }
    }
    throw '未找到 Microsoft Edge 或 Google Chrome。请安装浏览器，或调整 settings.json 中的 browser。'
}

function Test-WaitBreakProcessProfile {
    param([string]$CommandLine, [string]$Profile)
    Initialize-WaitBreakNative
    $expected = Get-WaitBreakCanonicalProfile $Profile
    if ($null -eq $expected) { return $false }
    $arguments = [WaitBreakPluginNative]::Arguments($CommandLine)
    $found = $null
    for ($index=0; $index -lt $arguments.Length; $index++) {
        $argument = $arguments[$index]
        if ($argument.StartsWith('--user-data-dir=', [StringComparison]::OrdinalIgnoreCase)) {
            if ($null -ne $found) { return $false }
            $found = $argument.Substring(16)
        } elseif ($argument -ieq '--user-data-dir') {
            if ($null -ne $found -or $index+1 -ge $arguments.Length) { return $false }
            $index++
            $found = $arguments[$index]
        }
    }
    if ($null -eq $found) { return $false }
    $actual = Get-WaitBreakCanonicalProfile $found
    return $null -ne $actual -and [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)
}

function Get-WaitBreakOwnedProcessIds {
    param($Browser)
    Initialize-WaitBreakNative
    $profile = [string](Get-WaitBreakProperty $Browser 'Profile')
    $choice = [string](Get-WaitBreakProperty $Browser 'Browser')
    if ($choice -notin @('edge','chrome') -or -not (Test-WaitBreakOwnedProfile $profile)) { return }
    $exeName = if ($choice -eq 'edge') { 'msedge.exe' } else { 'chrome.exe' }
    $processIds = New-Object 'Collections.Generic.List[int]'
    # Command lines stay in memory solely to prove dedicated-profile ownership.
    try {
        $processes = @(Get-CimInstance -ClassName Win32_Process -Filter ("Name='" + $exeName + "'") -Property ProcessId,CommandLine -OperationTimeoutSec 1 -ErrorAction Stop)
        foreach ($process in $processes) {
            if (Test-WaitBreakProcessProfile ([string]$process.CommandLine) $profile) { $processIds.Add([int]$process.ProcessId) }
        }
    } catch { return }
    foreach ($processId in $processIds) { [int]$processId }
}

function Get-WaitBreakOwnedWindows {
    param($Browser)
    Initialize-WaitBreakNative
    $processIds = [int[]]@(Get-WaitBreakOwnedProcessIds $Browser)
    if ($processIds.Length -eq 0) { return }
    foreach ($window in [WaitBreakPluginNative]::Windows($processIds)) { $window }
}

function Get-WaitBreakBrowserArguments {
    param([string]$Profile, [string]$Url)
    $values = @("--user-data-dir=$Profile", '--no-first-run', '--no-default-browser-check', '--new-window', $Url)
    return (($values | ForEach-Object { [WaitBreakPluginNative]::Quote([string]$_) }) -join ' ')
}

function Test-WaitBreakBrowserReuse {
    param($PreviousBrowser, $Descriptor, $Windows)
    if (-not [string]::Equals([string](Get-WaitBreakProperty $PreviousBrowser 'Profile'), [string](Get-WaitBreakProperty $Descriptor 'Profile'), [StringComparison]::OrdinalIgnoreCase) -or
        (Get-WaitBreakProperty $PreviousBrowser 'Browser') -ne (Get-WaitBreakProperty $Descriptor 'Browser') -or
        -not [string]::Equals([string](Get-WaitBreakProperty $PreviousBrowser 'Url'), [string](Get-WaitBreakProperty $Descriptor 'Url'), [StringComparison]::Ordinal)) { return $false }
    $handles = @((Get-WaitBreakProperty $PreviousBrowser 'Handles'))
    $processIds = @((Get-WaitBreakProperty $PreviousBrowser 'ProcessIds'))
    if ($handles.Count -ne $processIds.Count) { return $false }
    for ($index=0; $index -lt $handles.Count; $index++) {
        foreach ($window in @($Windows)) {
            if ([long]$handles[$index] -eq [long]$window.Handle -and [int]$processIds[$index] -eq [int]$window.ProcessId) { return $true }
        }
    }
    return $false
}

function New-WaitBreakBrowserRecord {
    param($Descriptor, $Windows, [long[]]$PreferredHandles = @())
    $ordered = New-Object 'Collections.Generic.List[object]'
    foreach ($preferred in $PreferredHandles) {
        foreach ($window in @($Windows)) { if ($window.Handle -eq $preferred -and -not $ordered.Contains($window)) { $ordered.Add($window) } }
    }
    foreach ($window in @($Windows)) { if (-not $ordered.Contains($window)) { $ordered.Add($window) } }
    return @{
        Handles = @($ordered | ForEach-Object { [long]$_.Handle })
        ProcessIds = @($ordered | ForEach-Object { [int]$_.ProcessId })
        Url = [string](Get-WaitBreakProperty $Descriptor 'Url')
        Browser = [string](Get-WaitBreakProperty $Descriptor 'Browser')
        Profile = [string](Get-WaitBreakProperty $Descriptor 'Profile')
    }
}

function Open-WaitBreakWebsite {
    param($Settings, [string]$DataRoot, $PreviousBrowser)
    Initialize-WaitBreakNative
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $url = Get-WaitBreakWebUrl ([string](Get-WaitBreakProperty $Settings 'url'))
    if ([string]::IsNullOrWhiteSpace($DataRoot) -or -not [IO.Path]::IsPathRooted($DataRoot)) { throw '网站配置目录必须为绝对路径。' }
    $profile = [IO.Path]::GetFullPath((Join-Path $DataRoot 'BrowserProfile'))
    $choice = [string](Get-WaitBreakProperty $Settings 'browser')
    if ([string]::IsNullOrWhiteSpace($choice)) { $choice = 'auto' }
    if ($choice -notin @('auto','edge','chrome')) { throw 'browser 只能为 auto、edge 或 chrome。' }
    $autoChoice = $choice -eq 'auto'
    if ($choice -eq 'auto' -and (Get-WaitBreakProperty $PreviousBrowser 'Browser') -in @('edge','chrome') -and
        [string]::Equals([string](Get-WaitBreakProperty $PreviousBrowser 'Profile'), $profile, [StringComparison]::OrdinalIgnoreCase)) {
        $choice = [string](Get-WaitBreakProperty $PreviousBrowser 'Browser')
    }
    try { $executable = Get-WaitBreakBrowserExecutable $choice }
    catch {
        if (-not $autoChoice -or $choice -eq 'auto') { throw }
        $executable = Get-WaitBreakBrowserExecutable 'auto'
    }
    [IO.Directory]::CreateDirectory($profile) | Out-Null
    $marker = Join-Path $profile '.gpt-wait-break-profile'
    if (-not [IO.File]::Exists($marker)) { [IO.File]::WriteAllText($marker, 'gpt-wait-break/plugin-profile/v2', (New-Object Text.UTF8Encoding($false))) }
    if (-not (Test-WaitBreakOwnedProfile $profile)) { throw '专用浏览器配置标记无效；未操作任何浏览器窗口。' }
    $descriptor = @{ Browser=$executable.Browser; Profile=$profile; Url=$url }
    $windows = @(Get-WaitBreakOwnedWindows $descriptor)
    if ($windows.Count -gt 0 -and (Test-WaitBreakBrowserReuse $PreviousBrowser $descriptor $windows)) {
        return New-WaitBreakBrowserRecord $descriptor $windows @((Get-WaitBreakProperty $PreviousBrowser 'Handles'))
    }
    # Do not open two Chromium products against one live profile.
    $other = @{ Browser=$(if ($executable.Browser -eq 'edge') { 'chrome' } else { 'edge' }); Profile=$profile }
    if (@(Get-WaitBreakOwnedProcessIds $other).Count -gt 0) { throw '请先退出另一个等待浏览器，再切换浏览器。' }
    $baseline = @($windows | ForEach-Object { [long]$_.Handle })
    $commandArguments = Get-WaitBreakBrowserArguments $profile $url
    $launched = Start-Process -FilePath $executable.Path -ArgumentList $commandArguments -WindowStyle Minimized -PassThru -ErrorAction Stop
    if ($null -ne $launched) { $launched.Dispose() }
    do {
        $windows = @(Get-WaitBreakOwnedWindows $descriptor)
        $newWindows = @($windows | Where-Object { $baseline -notcontains [long]$_.Handle })
        foreach ($window in $newWindows) { [WaitBreakPluginNative]::Minimize([long]$window.Handle, [int]$window.ProcessId) }
        if ($newWindows.Count -gt 0) { return New-WaitBreakBrowserRecord $descriptor $windows @($newWindows | ForEach-Object { [long]$_.Handle }) }
        if ($timer.ElapsedMilliseconds -ge 4000) { break }
        Start-Sleep -Milliseconds 120
    } while ($timer.ElapsedMilliseconds -lt 4000)
    throw '浏览器窗口启动超时，请关闭首次运行提示后重试。'
}

function Get-WaitBreakValidPreparedWindows {
    param([object[]]$Windows)
    foreach ($window in @($Windows)) {
        try {
            $handle = [long](Get-WaitBreakProperty $window 'Handle')
            $processId = [int](Get-WaitBreakProperty $window 'ProcessId')
            if ([WaitBreakPluginNative]::Valid($handle, $processId)) { $window }
        } catch { }
    }
}

function Show-WaitBreakWebsite {
    param($Browser, $Target = $null, [object[]]$PreparedWindows)
    Initialize-WaitBreakNative
    if ($PSBoundParameters.ContainsKey('PreparedWindows')) {
        # The hook obtains this ownership snapshot before entering the state mutex.
        # Never fall back to CIM here, including when the prepared snapshot is empty.
        $windows = @(Get-WaitBreakValidPreparedWindows $PreparedWindows)
    } else { $windows = @(Get-WaitBreakOwnedWindows $Browser) }
    if ($windows.Count -eq 0) { return }
    # Optional target closes the CIM-query gap between the hook's foreground check and show.
    if ($null -ne $Target -and -not (Test-WaitBreakTargetForeground $Target)) { return }
    $ordered = New-WaitBreakBrowserRecord $Browser $windows @((Get-WaitBreakProperty $Browser 'Handles'))
    # Restore owned popups too, so a login window is not lost behind the main website.
    for ($index=$ordered.Handles.Count-1; $index -ge 0; $index--) {
        [WaitBreakPluginNative]::Restore([long]$ordered.Handles[$index], [int]$ordered.ProcessIds[$index])
    }
    $first = [long]$ordered.Handles[0]
    $firstPid = [int]$ordered.ProcessIds[0]
    if (-not [WaitBreakPluginNative]::Focus($first, $firstPid)) { [WaitBreakPluginNative]::Flash($first, $firstPid) }
}

function Hide-WaitBreakWebsite {
    param($Browser, $Target, [bool]$ReturnFocusBool = $true, [object[]]$PreparedWindows)
    Initialize-WaitBreakNative
    if ($PSBoundParameters.ContainsKey('PreparedWindows')) {
        # Terminal hooks query ownership outside the state mutex, then revalidate here.
        $windows = @(Get-WaitBreakValidPreparedWindows $PreparedWindows)
    } else { $windows = @(Get-WaitBreakOwnedWindows $Browser) }
    # Query ownership first, then capture foreground immediately before the window operations.
    $foreground = [WaitBreakPluginNative]::Foreground()
    $wasOwnedForeground = @($windows | Where-Object { [long]$_.Handle -eq $foreground }).Count -gt 0
    foreach ($window in $windows) { [WaitBreakPluginNative]::Minimize([long]$window.Handle, [int]$window.ProcessId) }
    if (-not $ReturnFocusBool -or -not (Test-WaitBreakChatTarget $Target)) { return }
    $handle = [long](Get-WaitBreakProperty $Target 'Handle')
    $processId = [int](Get-WaitBreakProperty $Target 'ProcessId')
    if ($wasOwnedForeground -or $foreground -eq $handle) {
        if (-not [WaitBreakPluginNative]::Focus($handle, $processId)) { [WaitBreakPluginNative]::Flash($handle, $processId) }
    } else { [WaitBreakPluginNative]::Flash($handle, $processId) }
}

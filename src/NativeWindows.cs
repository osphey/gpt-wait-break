using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GptWaitBreak
{
    /// <summary>Window operations deliberately use HWND plus PID, never window-title text.</summary>
    public static class NativeWindows
    {
        private const uint GaRoot = 2;
        private const uint GwOwner = 4;
        private const int SwRestore = 9;
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x80L;
        private static readonly int OwnProcessId = Process.GetCurrentProcess().Id;

        public static IntPtr ForegroundWindow { get { return GetForegroundWindow(); } }

        public static int ProcessId(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return 0;
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid <= int.MaxValue ? (int)pid : 0;
        }

        public static bool IsValidWindow(IntPtr window, int expectedPid)
        {
            return expectedPid > 0 && window != IntPtr.Zero && IsWindow(window)
                && ProcessId(window) == expectedPid;
        }

        public static bool IsChatWindow(IntPtr window, AppSettings settings)
        {
            if (settings == null || settings.ChatProcessNames == null || window == IntPtr.Zero
                || !IsWindow(window) || !IsWindowVisible(window) || GetAncestor(window, GaRoot) != window
                || GetWindow(window, GwOwner) != IntPtr.Zero
                || (GetExtendedStyle(window) & WsExToolWindow) != 0) return false;

            int pid = ProcessId(window);
            if (pid <= 0 || pid == OwnProcessId) return false;
            try
            {
                using (Process process = Process.GetProcessById(pid))
                {
                    string name = NormalizeProcessName(process.ProcessName);
                    foreach (string allowed in settings.ChatProcessNames)
                        if (string.Equals(name, NormalizeProcessName(allowed), StringComparison.OrdinalIgnoreCase)
                            && name.Length > 0) return true;
                }
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return false;
        }

        public static IntPtr FindChatWindow(AppSettings settings)
        {
            IntPtr foreground = ForegroundWindow;
            if (IsChatWindow(foreground, settings)) return foreground;
            IntPtr found = IntPtr.Zero;
            bool ambiguous = false;
            EnumWindows(delegate(IntPtr candidate, IntPtr unused)
            {
                if (!IsChatWindow(candidate, settings)) return true;
                if (found != IntPtr.Zero) { ambiguous = true; return false; }
                found = candidate;
                return true;
            }, IntPtr.Zero);
            return ambiguous ? IntPtr.Zero : found;
        }

        public static bool IsOwnProcessForeground()
        {
            return ProcessId(ForegroundWindow) == OwnProcessId;
        }

        // The caller must decide whether returning is appropriate (for example, its website is
        // still foreground). Do not use AttachThreadInput, synthetic keys, or foreground-lock hacks.
        public static bool TryFocus(IntPtr window, int expectedPid)
        {
            if (!IsValidWindow(window, expectedPid)) return false;
            if (ForegroundWindow == window) return true;
            if (IsIconic(window)) ShowWindowAsync(window, SwRestore);
            if (!IsValidWindow(window, expectedPid)) return false;
            bool accepted = SetForegroundWindow(window);
            if (!accepted) Flash(window);
            return accepted;
        }

        public static void Flash(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return;
            FlashInfo info = new FlashInfo {
                Size = (uint)Marshal.SizeOf(typeof(FlashInfo)), Window = window,
                Flags = 2, Count = 3, Timeout = 0
            };
            FlashWindowEx(ref info);
        }

        internal static bool IsInspectable(IntPtr window, int expectedPid)
        {
            return IsValidWindow(window, expectedPid) && IsWindowVisible(window) && !IsIconic(window);
        }

        private static string NormalizeProcessName(string name)
        {
            string result = (name ?? string.Empty).Trim();
            if (result.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                result = result.Substring(0, result.Length - 4);
            // A configured process is an exact executable base name, never a path or pattern.
            if (result.IndexOfAny(new[] { '\\', '/', '*', '?' }) >= 0) return string.Empty;
            return result;
        }

        private static long GetExtendedStyle(IntPtr window)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr64(window, GwlExStyle).ToInt64()
                : GetWindowLong32(window, GwlExStyle);
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        private struct FlashInfo
        {
            public uint Size;
            public IntPtr Window;
            public uint Flags;
            public uint Count;
            public uint Timeout;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlashWindowEx(ref FlashInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr window, int index);
    }
}

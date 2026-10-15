using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GptWaitBreak
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            string command = args.Length == 0 ? "--configure" : args[0];
            if (command == "--hook") {
                // Hooks are observers. Failures must never reject a prompt or continue a turn.
                try {
                    long emittedAt = DateTime.UtcNow.Ticks;
                    // A WinExe has no attached console. Decode the redirected handle directly;
                    // Console.InputEncoding can call SetConsoleCP and fail with an invalid handle.
                    BreakEvent message;
                    using (StreamReader input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), true))
                        message = HookInput.Parse(args.Length > 1 ? args[1] : "", HookInput.ReadInput(input));
                    message.SentAtTicks = emittedAt;
                    if (message.Kind == "start") {
                        AppSettings settings = Configuration.Load();
                        IntPtr foreground = NativeWindows.ForegroundWindow;
                        if (!settings.Enabled || !NativeWindows.IsChatWindow(foreground, settings)) { Console.WriteLine("{}"); return 0; }
                        message.WindowHandle = foreground.ToInt64();
                        message.ProcessId = NativeWindows.ProcessId(foreground);
                    }
                    // An extremely short answer can end while the first tray process is starting.
                    // Retry terminal delivery too; terminal-first events are safely tombstoned.
                    EventTransport.Send(message, true);
                } catch { /* Never put prompts, website data or exception payloads on hook stdout. */ }
                Console.WriteLine("{}");
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (command == "--diagnose") {
                string report = DiagnosticJson();
                if (args.Length > 1) File.WriteAllText(Path.GetFullPath(args[1]), report, new UTF8Encoding(false));
                else Console.WriteLine(report);
                return 0;
            }
            if (command == "--self-test-browser") return BrowserSmokeTest.Run(args);
            if (command == "--configure") {
                if (EventTransport.Send(new BreakEvent { Kind = "configure" }, false)) return 0;
                try {
                    using (SettingsForm form = new SettingsForm(Configuration.Load())) {
                        if (form.ShowDialog() == DialogResult.OK) Configuration.Save(form.Result);
                    }
                    return 0;
                } catch (Exception ex) { MessageBox.Show(ex.Message, "GPT Wait Break 设置", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
            }
            if (command == "--return" || command == "--open" || command == "--quit")
                return EventTransport.Send(new BreakEvent { Kind = command.Substring(2) }, command == "--open") ? 0 : 1;
            if (command != "--run") return 2;
            bool created;
            using (Mutex mutex = new Mutex(true, AppPaths.MutexName, out created)) {
                if (!created) return 0;
                try { Application.Run(new BreakApplication()); return 0; }
                catch (Exception ex) { MessageBox.Show(ex.Message, "GPT Wait Break", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public static string DiagnosticJson()
        {
            string version = "unavailable";
            try { version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(null); } catch { }
            string settings = "valid";
            bool uia = false;
            try { uia = Configuration.Load().EnableUiAutomation; } catch { settings = "invalid"; }
            return Configuration.Json.Serialize(new {
                version = "0.1.0", platform = "Windows x64", webView2Runtime = version,
                configuration = settings, ordinaryChatDetection = uia ? "experimental-enabled" : "disabled",
                dataDirectory = AppPaths.DataDirectory,
                note = "不含聊天内容、密码或网站会话。普通聊天与网站登录兼容性须在用户设备验证。"
            });
        }
    }

    internal sealed class BreakApplication : ApplicationContext
    {
        private AppSettings settings;
        private readonly BreakEngine engine;
        private readonly Control dispatcher;
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem statusMenu;
        private readonly ToolStripMenuItem enabledMenu;
        private readonly EventTransport transport;
        private readonly System.Windows.Forms.Timer timer;
        private BrowserWindow browser;
        private volatile bool disposed;
        private bool probePending;
        private DateTime lastProbe = DateTime.MinValue;
        private long ordinaryWindow;
        private int ordinaryPid;
        private string ordinaryTurn;
        private int idleObservations;
        private ChatState lastOrdinaryState = ChatState.Unknown;
        private long suppressedWindow;
        private int suppressedPid;
        private int rearmIdleObservations;
        private string diagnostic = "等待发送消息";
        private long lastTarget;
        private int lastTargetPid;
        private SettingsForm settingsForm;

        public BreakApplication()
        {
            bool configFailed = false;
            try { settings = Configuration.Load(); }
            catch { settings = AppSettings.CreateDefault(); settings.Enabled = false; configFailed = true; }
            engine = new BreakEngine(settings.DelayMilliseconds, settings.StopDebounceMilliseconds);
            dispatcher = new Control();
            dispatcher.CreateControl();
            IntPtr unused = dispatcher.Handle;
            ContextMenuStrip menu = new ContextMenuStrip();
            statusMenu = new ToolStripMenuItem("等待发送消息") { Enabled = false };
            menu.Items.Add(statusMenu);
            enabledMenu = new ToolStripMenuItem("启用等待浏览") { Checked = settings.Enabled, CheckOnClick = false };
            enabledMenu.Click += delegate { settings.Enabled = !settings.Enabled; SaveSettings(); Apply(engine.Cancel("toggle")); UpdateStatus(); };
            menu.Items.Add(enabledMenu);
            menu.Items.Add("打开网站 / 登录", null, delegate { OpenManual(); });
            menu.Items.Add("返回聊天", null, delegate { ReturnManual(); });
            menu.Items.Add("设置网站与检测", null, delegate { Configure(); });
            menu.Items.Add("保存诊断报告", null, delegate { SaveDiagnostics(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "GPT Wait Break", ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { Configure(); };
            transport = new EventTransport(delegate(BreakEvent message) {
                try { if (!disposed) dispatcher.BeginInvoke((Action)delegate { Receive(message); }); } catch (InvalidOperationException) { }
            });
            transport.Start();
            timer = new System.Windows.Forms.Timer { Interval = 150 };
            timer.Tick += delegate { Tick(); };
            timer.Start();
            if (configFailed) Notify("设置文件无法读取，已暂停。请通过托盘菜单修复设置。");
            UpdateStatus();
        }

        private void Receive(BreakEvent message)
        {
            if (message == null || disposed) return;
            if (message.Kind == "configure") { Configure(); return; }
            if (message.Kind == "open") { OpenManual(); return; }
            if (message.Kind == "return") { ReturnManual(); return; }
            if (message.Kind == "quit") { ExitThread(); return; }
            if (message.Source != "hook" && message.Source != "uia") return;
            if (message.Kind == "start") {
                IntPtr target = new IntPtr(message.WindowHandle);
                if (!settings.Enabled || !NativeWindows.IsChatWindow(target, settings)
                    || !NativeWindows.IsValidWindow(target, message.ProcessId)) return;
                if (engine.Active != null && message.Source == "uia" && engine.Active.Source == "hook") return;
                if (!engine.CanAcceptStart(message, DateTime.UtcNow.Ticks)) return;
                // Only a current foreground submission can supersede an earlier tracked turn.
                if (engine.Active != null && (engine.Active.SessionId != message.SessionId || engine.Active.TurnId != message.TurnId
                    || engine.Active.Source != message.Source)) {
                    if (NativeWindows.ForegroundWindow != target) return;
                    Apply(engine.Cancel("new-foreground-turn"));
                }
                if (message.Source == "hook") { suppressedWindow = 0; suppressedPid = 0; rearmIdleObservations = 0; }
            }
            Apply(engine.Receive(message, DateTime.UtcNow.Ticks));
            if (message.Kind == "start" && engine.Active != null && engine.Active.Source == message.Source && engine.Active.SessionId == message.SessionId && engine.Active.TurnId == message.TurnId) {
                lastTarget = engine.Active.WindowHandle;
                lastTargetPid = engine.Active.ProcessId;
            }
            UpdateStatus();
        }

        private void Tick()
        {
            if (disposed) return;
            ActiveBreak active = engine.Active;
            if (active != null) {
                IntPtr target = new IntPtr(active.WindowHandle);
                bool valid = NativeWindows.IsValidWindow(target, active.ProcessId);
                bool mayOpen = settings.Enabled && (NativeWindows.ForegroundWindow == target || (browser != null && browser.IsBrowserForeground));
                // UIA is allowed to open only directly after a fresh positive Busy observation.
                if (active.Source == "uia") mayOpen = false;
                Apply(engine.Tick(DateTime.UtcNow.Ticks, valid, mayOpen));
            }
            if (settings.Enabled && settings.EnableUiAutomation && !probePending && DateTime.UtcNow - lastProbe > TimeSpan.FromMilliseconds(650)) StartProbe();
            UpdateStatus();
        }

        private void StartProbe()
        {
            if (engine.Active != null && engine.Active.Source == "hook") return;
            IntPtr target = engine.Active != null ? new IntPtr(engine.Active.WindowHandle) : NativeWindows.ForegroundWindow;
            if (!NativeWindows.IsChatWindow(target, settings)) return;
            lastProbe = DateTime.UtcNow;
            probePending = true;
            int targetPid = NativeWindows.ProcessId(target);
            AppSettings snapshot = settings;
            Task.Run(delegate {
                ChatObservation observation;
                try { observation = ChatProbe.Inspect(target, snapshot); }
                catch { observation = new ChatObservation { State = ChatState.Unknown, WindowHandle = target.ToInt64(), ProcessId = targetPid, Detail = "uia:probe-error" }; }
                try {
                    if (!disposed) dispatcher.BeginInvoke((Action)delegate {
                        if (disposed) return;
                        probePending = false;
                        if (settings.Enabled && settings.EnableUiAutomation && Object.ReferenceEquals(settings, snapshot)) Observe(observation);
                    });
                } catch (InvalidOperationException) { }
            });
        }

        private void Observe(ChatObservation observation)
        {
            if (disposed) return;
            diagnostic = observation.Detail ?? "uia:unknown";
            if (engine.Active != null && engine.Active.Source == "hook") return;
            lastOrdinaryState = observation.State;
            // A manual/automatic return disarms UIA until that window is positively idle.
            // Otherwise the remaining Stop button would immediately start another break.
            if (observation.WindowHandle == suppressedWindow && observation.ProcessId == suppressedPid) {
                if (observation.State == ChatState.Idle) {
                    rearmIdleObservations++;
                    if (rearmIdleObservations >= 2) { suppressedWindow = 0; suppressedPid = 0; rearmIdleObservations = 0; }
                } else rearmIdleObservations = 0;
                UpdateStatus();
                return;
            }
            if (observation.State == ChatState.Busy) {
                idleObservations = 0;
                if (engine.Active == null) {
                    if (NativeWindows.ForegroundWindow.ToInt64() != observation.WindowHandle) return;
                    ordinaryWindow = observation.WindowHandle;
                    ordinaryPid = observation.ProcessId;
                    ordinaryTurn = Guid.NewGuid().ToString("N");
                } else if (engine.Active.WindowHandle != observation.WindowHandle || engine.Active.ProcessId != observation.ProcessId) return;
                Receive(OrdinaryEvent("start"));
                ActiveBreak active = engine.Active;
                if (active != null && active.Source == "uia") {
                    IntPtr target = new IntPtr(active.WindowHandle);
                    bool mayOpen = NativeWindows.ForegroundWindow == target || (browser != null && browser.IsBrowserForeground);
                    Apply(engine.Tick(DateTime.UtcNow.Ticks, NativeWindows.IsValidWindow(target, active.ProcessId), mayOpen));
                }
            } else if (engine.Active != null && engine.Active.Source == "uia"
                && observation.WindowHandle == ordinaryWindow && observation.ProcessId == ordinaryPid) {
                if (observation.State == ChatState.NeedsUser) { idleObservations = 0; Receive(OrdinaryEvent("permission")); }
                else if (observation.State == ChatState.Idle) {
                    idleObservations++;
                    if (idleObservations >= 2) Receive(OrdinaryEvent("stop"));
                } else { idleObservations = 0; engine.Active.FinishAtTicks = 0; }
            }
            UpdateStatus();
        }

        private BreakEvent OrdinaryEvent(string kind)
        {
            return new BreakEvent { Kind = kind, Source = "uia", SessionId = "uia-" + ordinaryWindow,
                TurnId = ordinaryTurn, WindowHandle = ordinaryWindow, ProcessId = ordinaryPid, SentAtTicks = DateTime.UtcNow.Ticks };
        }

        private BrowserWindow EnsureBrowser()
        {
            if (browser == null || browser.IsDisposed) browser = new BrowserWindow(settings, ReturnManual, Configure);
            return browser;
        }

        private void Apply(BreakAction action)
        {
            if (action.Kind == BreakActionKind.None) return;
            if (action.Kind == BreakActionKind.Open) {
                try { EnsureBrowser().OpenSite(Configuration.DefaultSite(settings)); }
                catch { Apply(engine.Cancel("browser-unavailable")); Notify("网站窗口无法打开。请检查 WebView2 Runtime 或打开设置。"); }
                return;
            }
            bool hadForeground = browser != null && browser.IsBrowserForeground;
            if (browser != null && !browser.IsDisposed) browser.Conceal();
            if (action.Kind == BreakActionKind.Return && NativeWindows.IsValidWindow(new IntPtr(action.WindowHandle), action.ProcessId)) {
                suppressedWindow = action.WindowHandle;
                suppressedPid = action.ProcessId;
                rearmIdleObservations = 0;
                if (hadForeground || action.Reason == "manual") {
                    if (!NativeWindows.TryFocus(new IntPtr(action.WindowHandle), action.ProcessId)) Notify("回答已停止，点击 GPT 窗口查看。");
                } else NativeWindows.Flash(new IntPtr(action.WindowHandle));
            }
            ordinaryTurn = null;
            idleObservations = 0;
        }

        private void OpenManual()
        {
            IntPtr target = NativeWindows.ForegroundWindow;
            if (NativeWindows.IsChatWindow(target, settings)) { lastTarget = target.ToInt64(); lastTargetPid = NativeWindows.ProcessId(target); }
            EnsureBrowser().OpenSite(Configuration.DefaultSite(settings));
        }

        private void ReturnManual()
        {
            ActiveBreak old = engine.Active;
            if (old != null) { suppressedWindow = old.WindowHandle; suppressedPid = old.ProcessId; rearmIdleObservations = 0; }
            BreakAction action = engine.ReturnNow();
            if (action.Kind == BreakActionKind.None) action = new BreakAction { Kind = BreakActionKind.Return, WindowHandle = lastTarget, ProcessId = lastTargetPid, Reason = "manual" };
            Apply(action);
            UpdateStatus();
        }

        private void Configure()
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.Activate(); return; }
            using (settingsForm = new SettingsForm(settings)) {
                if (settingsForm.ShowDialog() == DialogResult.OK) {
                    Apply(engine.Cancel("settings-changed"));
                    settings = settingsForm.Result;
                    engine.DelayMilliseconds = settings.DelayMilliseconds;
                    engine.StopDebounceMilliseconds = settings.StopDebounceMilliseconds;
                    SaveSettings();
                    if (browser != null && !browser.IsDisposed) { browser.Shutdown(); browser = null; }
                }
            }
            settingsForm = null;
            UpdateStatus();
        }

        private void SaveSettings()
        {
            try { Configuration.Save(settings); }
            catch { Notify("设置保存失败，请检查数据目录写入权限。"); }
        }

        private void SaveDiagnostics()
        {
            using (SaveFileDialog dialog = new SaveFileDialog { FileName = "gpt-wait-break-diagnostics.json", Filter = "JSON 文件|*.json" }) {
                if (dialog.ShowDialog() == DialogResult.OK) {
                    try { File.WriteAllText(dialog.FileName, Program.DiagnosticJson(), new UTF8Encoding(false)); }
                    catch { Notify("无法写入诊断报告。"); }
                }
            }
        }

        private void UpdateStatus()
        {
            if (tray == null || disposed) return;
            string text = !settings.Enabled ? "已暂停" : engine.Active != null
                ? (engine.Active.Shown ? "等待回答 · 浏览中" : "检测到生成 · 等待显示")
                : settings.EnableUiAutomation ? "等待消息 · 普通聊天检测为实验性" : "等待 Work / Codex 事件";
            if (settings.Enabled && settings.EnableUiAutomation && lastOrdinaryState == ChatState.Unknown
                && diagnostic.StartsWith("uia:", StringComparison.Ordinal)
                && (engine.Active == null || engine.Active.Source == "uia")) text = "界面状态不可读 · 可手动返回";
            statusMenu.Text = text;
            statusMenu.ToolTipText = diagnostic;
            enabledMenu.Checked = settings.Enabled;
            tray.Text = "GPT Wait Break · " + text;
        }

        private void Notify(string text) { tray.ShowBalloonTip(3500, "GPT Wait Break", text, ToolTipIcon.Info); }

        protected override void ExitThreadCore()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop(); timer.Dispose(); transport.Dispose();
            if (settingsForm != null && !settingsForm.IsDisposed) { settingsForm.DialogResult = DialogResult.Cancel; settingsForm.Close(); }
            if (browser != null && !browser.IsDisposed) browser.Shutdown();
            tray.Visible = false; tray.Dispose(); dispatcher.Dispose();
            base.ExitThreadCore();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GptWaitBreak
{
    // This is a companion browser window. It does not automate ChatGPT's internal browser.
    public sealed class BrowserWindow : Form
    {
        private sealed class BrowserTab
        {
            public TabPage Page;
            public WebView2 View;
            public Panel MessagePanel;
            public string SiteKey;
            public string InitialUrl;
            public Task Initialization;
            public bool Ready;
            public bool Closed;
            public bool Popup;
            public bool MutedByHost;
            public bool PreviousMute;
            public bool NavigationLoaded;
        }

        private const string PauseMediaScript = "(function(){try{var k='__gptWaitBreakPausedMedia_v1';var a=window[k]||[];document.querySelectorAll('video,audio').forEach(function(m){if(!m.paused){if(a.indexOf(m)<0)a.push(m);m.pause();}});window[k]=a;}catch(e){}})();";
        private const string ResumeMediaScript = "(function(){try{var k='__gptWaitBreakPausedMedia_v1';var a=window[k]||[];window[k]=[];a.forEach(function(m){if(m.isConnected&&!m.ended){var p=m.play();if(p&&p.catch)p.catch(function(){});}});}catch(e){}})();";
        private const string RuntimeUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";
        private readonly AppSettings settings;
        private readonly Action returnToChat;
        private readonly Action showSettings;
        private readonly TabControl tabs;
        private readonly TextBox address;
        private readonly ToolStripButton back;
        private readonly ToolStripButton forward;
        private readonly ToolStripStatusLabel status;
        private readonly Dictionary<TabPage, BrowserTab> tabStates = new Dictionary<TabPage, BrowserTab>();
        private Task<CoreWebView2Environment> environment;
        private bool concealed = true;
        private bool shuttingDown;
        private bool resizing;

        public BrowserWindow(AppSettings settings, Action returnToChat, Action showSettings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            this.settings = settings;
            this.returnToChat = returnToChat;
            this.showSettings = showSettings;
            Text = "刷一会 · GPT Wait Break";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1150, 790);
            MinimumSize = new Size(760, 500);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            KeyPreview = true;

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            Controls.Add(layout);

            TableLayoutPanel heading = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(241, 245, 249), ColumnCount = 2, Padding = new Padding(16, 9, 12, 8), Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 240));
            Label title = new Label { Text = "刷一会", Font = new Font(Font.FontFamily, 19F, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            FlowLayoutPanel headingButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            Button settingsButton = new Button { Text = "设置", Size = new Size(72, 33), Margin = new Padding(8, 4, 0, 0), FlatStyle = FlatStyle.System };
            settingsButton.Click += delegate { if (this.showSettings != null) this.showSettings(); };
            Button returnButton = new Button { Text = "返回 GPT  ↗", Size = new Size(142, 33), Margin = new Padding(0, 4, 0, 0), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            returnButton.FlatAppearance.BorderSize = 0;
            returnButton.Click += delegate { ReturnToChat(); };
            headingButtons.Controls.Add(settingsButton);
            headingButtons.Controls.Add(returnButton);
            heading.Controls.Add(title, 0, 0);
            heading.Controls.Add(headingButtons, 1, 0);
            layout.Controls.Add(heading, 0, 0);

            ToolStrip toolbar = new ToolStrip { Dock = DockStyle.Fill, GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White, Padding = new Padding(10, 2, 8, 2), RenderMode = ToolStripRenderMode.System, Margin = Padding.Empty };
            back = new ToolStripButton("后退") { Enabled = false, ToolTipText = "Alt + ←" };
            forward = new ToolStripButton("前进") { Enabled = false, ToolTipText = "Alt + →" };
            ToolStripButton reload = new ToolStripButton("刷新") { ToolTipText = "Ctrl + R" };
            ToolStripDropDownButton siteMenu = new ToolStripDropDownButton("常用网站 ▾");
            ToolStripButton closeTab = new ToolStripButton("关闭当前标签");
            back.Click += delegate { NavigateHistory(false); };
            forward.Click += delegate { NavigateHistory(true); };
            reload.Click += delegate { ReloadCurrent(); };
            closeTab.Click += delegate { BrowserTab tab = CurrentTab; if (tab != null) CloseTab(tab); };
            siteMenu.DropDownOpening += delegate {
                siteMenu.DropDownItems.Clear();
                if (this.settings.Sites != null)
                {
                    foreach (SiteEntry entry in this.settings.Sites)
                    {
                        if (entry == null) continue;
                        SiteEntry captured = entry;
                        ToolStripMenuItem item = new ToolStripMenuItem(entry.Name ?? entry.Url);
                        item.Click += delegate { OpenSite(captured); };
                        siteMenu.DropDownItems.Add(item);
                    }
                }
                if (siteMenu.DropDownItems.Count == 0) siteMenu.DropDownItems.Add(new ToolStripMenuItem("请先在设置中添加网站") { Enabled = false });
            };
            toolbar.Items.Add(back);
            toolbar.Items.Add(forward);
            toolbar.Items.Add(reload);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(siteMenu);
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(closeTab);
            layout.Controls.Add(toolbar, 0, 1);

            TableLayoutPanel addressBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12, 2, 12, 4), Margin = Padding.Empty };
            addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            addressBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 63));
            address = new TextBox { Dock = DockStyle.Fill, MaxLength = 2048, Margin = Padding.Empty };
            address.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; NavigateAddress(); } };
            Button go = new Button { Text = "前往", Dock = DockStyle.Fill, Margin = new Padding(6, 0, 0, 0) };
            go.Click += delegate { NavigateAddress(); };
            addressBar.Controls.Add(address, 0, 0);
            addressBar.Controls.Add(go, 1, 0);
            layout.Controls.Add(addressBar, 0, 2);

            tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 7), ShowToolTips = true, Margin = new Padding(8, 0, 8, 0) };
            tabs.SelectedIndexChanged += delegate { RefreshControls(); UpdateAllMedia(); };
            layout.Controls.Add(tabs, 0, 3);
            StatusStrip statusStrip = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = true, BackColor = Color.FromArgb(248, 250, 252), Margin = Padding.Empty };
            status = new ToolStripStatusLabel("首次打开请自行登录；关闭窗口会收起并保留网页。") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            statusStrip.Items.Add(status);
            layout.Controls.Add(statusStrip, 0, 4);
        }

        private BrowserTab CurrentTab
        {
            get
            {
                BrowserTab tab;
                return tabs.SelectedTab != null && tabStates.TryGetValue(tabs.SelectedTab, out tab) ? tab : null;
            }
        }

        public bool IsBrowserForeground
        {
            get { return !IsDisposed && IsHandleCreated && NativeWindows.ForegroundWindow == Handle; }
        }

        internal WebView2 WebViewForTesting
        {
            get { BrowserTab tab = CurrentTab; return tab == null ? null : tab.View; }
        }

        internal int TabCountForTesting { get { return tabStates.Count; } }

        internal WebView2[] AllWebViewsForTesting
        {
            get
            {
                List<WebView2> views = new List<WebView2>();
                foreach (BrowserTab tab in tabStates.Values) if (!tab.Closed) views.Add(tab.View);
                return views.ToArray();
            }
        }

        public async void OpenSite(SiteEntry site)
        {
            if (shuttingDown || IsDisposed || site == null) return;
            string normalized;
            if (!SettingsForm.TryNormalizeSiteUrl(site.Url, out normalized))
            {
                MessageBox.Show(this, "此网站的网址无效，请在设置中使用 http:// 或 https:// 网址。", "无法打开网站", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                BrowserTab tab = null;
                foreach (BrowserTab candidate in tabStates.Values)
                    if (String.Equals(candidate.SiteKey, normalized, StringComparison.Ordinal)) { tab = candidate; break; }
                if (tab == null) tab = CreateTab(site.Name ?? new Uri(normalized).Host, normalized, false);
                tabs.SelectedTab = tab.Page;
                ShowBrowser();
                await EnsureTabAsync(tab);
            }
            catch (Exception error) { ShowOperationError(error); }
        }

        private BrowserTab CreateTab(string title, string url, bool popup)
        {
            BrowserTab tab = new BrowserTab { SiteKey = popup ? null : url, InitialUrl = url, Popup = popup };
            tab.Page = new TabPage(ShortTitle(title)) { BackColor = Color.White, Padding = Padding.Empty, ToolTipText = url ?? "登录或新窗口" };
            tab.View = NewView();
            tab.MessagePanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(248, 250, 252) };
            tab.MessagePanel.Controls.Add(new Label { Text = "正在准备浏览器…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(100, 116, 139) });
            tab.Page.Controls.Add(tab.View);
            tab.Page.Controls.Add(tab.MessagePanel);
            tabStates.Add(tab.Page, tab);
            tabs.TabPages.Add(tab.Page);
            return tab;
        }

        private static WebView2 NewView()
        {
            return new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.White, Visible = false };
        }

        private Task EnsureTabAsync(BrowserTab tab)
        {
            if (tab.Initialization == null) tab.Initialization = InitializeTabAsync(tab);
            return tab.Initialization;
        }

        private async Task InitializeTabAsync(BrowserTab tab)
        {
            try
            {
                if (environment == null || environment.IsFaulted || environment.IsCanceled)
                {
                    Directory.CreateDirectory(AppPaths.BrowserProfile);
                    environment = CoreWebView2Environment.CreateAsync(null, AppPaths.BrowserProfile, null);
                }
                CoreWebView2Environment sharedEnvironment = await environment;
                if (shuttingDown || tab.Closed) return;
                await tab.View.EnsureCoreWebView2Async(sharedEnvironment);
                if (shuttingDown || tab.Closed) return;
                BindCoreEvents(tab);
                tab.Ready = true;
                tab.View.Visible = true;
                tab.MessagePanel.Visible = false;
                UpdateMedia(tab, false);
                // NewWindowRequested needs a new, un-navigated WebView with the same environment.
                if (!tab.Popup) tab.View.CoreWebView2.Navigate(tab.InitialUrl);
                RefreshControls();
            }
            catch (Exception error)
            {
                if (!shuttingDown && !tab.Closed) ShowInitializationError(tab, error);
                throw;
            }
        }

        private void BindCoreEvents(BrowserTab tab)
        {
            CoreWebView2 core = tab.View.CoreWebView2;
            // Keep WebView2's normal browser security, permission, certificate and download UI.
            // No host objects, credential extraction or account synchronization are installed.
            core.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e) {
                if (!String.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase) && !IsWebNavigation(e.Uri))
                {
                    e.Cancel = true;
                    SetStatus(tab, "已阻止非 HTTP(S) 链接；本窗口仅用于浏览网站。");
                    return;
                }
                tab.NavigationLoaded = false;
                SetStatus(tab, "正在加载…");
            };
            core.LaunchingExternalUriScheme += delegate(object sender, CoreWebView2LaunchingExternalUriSchemeEventArgs e) {
                e.Cancel = true;
                SetStatus(tab, "此链接需要外部应用，已留在当前网站。可选择网站的网页版登录方式。");
            };
            core.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e) {
                if (tab.Closed || shuttingDown) return;
                tab.NavigationLoaded = true;
                if (e.IsSuccess) SetStatus(tab, "AI 完成后将收起此窗口；登录态保存在本机浏览器配置中。");
                else if (e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) SetStatus(tab, "页面未完成加载（" + e.WebErrorStatus.ToString() + "），可点击刷新重试。");
                RefreshControls();
                UpdateMedia(tab, true);
            };
            core.SourceChanged += delegate { if (!tab.Closed) RefreshControls(); };
            core.HistoryChanged += delegate { if (!tab.Closed) RefreshControls(); };
            core.DocumentTitleChanged += delegate {
                if (tab.Closed) return;
                string title = core.DocumentTitle;
                if (!String.IsNullOrWhiteSpace(title)) tab.Page.Text = ShortTitle(title);
            };
            core.NewWindowRequested += async delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) {
                try { await HandleNewWindowAsync(e); }
                catch (Exception error) { BlockNewWindow(e); ShowOperationError(error); }
            };
            core.WindowCloseRequested += delegate {
                if (tab.Closed || shuttingDown) return;
                // Defer disposal until the WebView event has returned.
                try { BeginInvoke(new Action(delegate { if (!tab.Closed && !shuttingDown) CloseTab(tab); })); }
                catch (InvalidOperationException) { /* The containing form may already be closing. */ }
            };
            core.ProcessFailed += delegate {
                if (!tab.Closed) SetStatus(tab, "网页进程意外结束。可尝试刷新，或关闭此标签后重新打开。");
            };
        }

        private async Task HandleNewWindowAsync(CoreWebView2NewWindowRequestedEventArgs e)
        {
            if (shuttingDown) { BlockNewWindow(e); return; }
            bool blank = String.IsNullOrEmpty(e.Uri) || String.Equals(e.Uri, "about:blank", StringComparison.OrdinalIgnoreCase);
            if (!blank && !IsWebNavigation(e.Uri))
            {
                BlockNewWindow(e);
                status.Text = "已阻止非 HTTP(S) 新窗口链接。";
                return;
            }
            // WebView2 has no normal popup blocker. Restrict unsolicited window floods while
            // retaining user initiated links and login windows started by authentication flows.
            if (tabStates.Count >= 20)
            {
                BlockNewWindow(e);
                status.Text = "已达到 20 个标签，请先关闭部分标签。";
                return;
            }
            CoreWebView2Deferral deferral = e.GetDeferral();
            BrowserTab popup = null;
            try
            {
                popup = CreateTab("新窗口 / 登录", null, true);
                await EnsureTabAsync(popup);
                if (shuttingDown || popup.Closed || !popup.Ready) { BlockNewWindow(e); return; }
                e.NewWindow = popup.View.CoreWebView2;
                // Do not Show or Activate here: an async popup must never reopen a concealed break.
                if (!concealed) tabs.SelectedTab = popup.Page;
            }
            catch (Exception)
            {
                BlockNewWindow(e);
                if (popup != null && !popup.Closed) status.Text = "登录新窗口无法创建，请重试或检查浏览器运行时。";
            }
            finally
            {
                try { deferral.Complete(); }
                catch (Exception) { /* The opener may already have been disposed during application shutdown. */ }
            }
        }

        private static void BlockNewWindow(CoreWebView2NewWindowRequestedEventArgs request)
        {
            try { request.Handled = true; }
            catch (Exception) { /* Closing the opener may invalidate its deferred request. */ }
        }

        private static bool IsWebNavigation(string value)
        {
            // OAuth redirect URLs may exceed the settings editor's input length limit.
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !String.IsNullOrEmpty(uri.Host) && String.IsNullOrEmpty(uri.UserInfo);
        }

        private void ShowInitializationError(BrowserTab tab, Exception error)
        {
            tab.Ready = false;
            tab.MessagePanel.Controls.Clear();
            tab.MessagePanel.Visible = true;
            tab.View.Visible = false;
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(40) };
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            content.Controls.Add(new Label { Text = "浏览器暂时无法启动", Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(30, 41, 59) }, 0, 1);
            string reason = error is WebView2RuntimeNotFoundException ? "请先安装 Microsoft Edge WebView2 Runtime，再重试。" : "请确认 WebView2 Runtime 已安装且为最新版本，\r\n并检查本机浏览器配置目录是否可写。";
            content.Controls.Add(new Label { Text = reason + "\r\n错误代码：0x" + error.HResult.ToString("X8"), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(83, 98, 119) }, 0, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.None };
            Button runtime = new Button { Text = "打开官方运行时下载页", AutoSize = true, Height = 32 };
            runtime.Click += delegate {
                try { Process.Start(new ProcessStartInfo(RuntimeUrl) { UseShellExecute = true }); }
                catch (Exception) { MessageBox.Show(this, "无法打开默认浏览器，请访问 Microsoft 官方 WebView2 下载页。", "打开下载页", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            };
            Button retry = new Button { Text = tab.Popup ? "关闭此标签" : "重试", Width = 100, Height = 32 };
            retry.Click += async delegate {
                if (tab.Closed || shuttingDown) return;
                if (tab.Popup) { CloseTab(tab); return; }
                retry.Enabled = false;
                tab.Page.Controls.Remove(tab.View);
                tab.View.Dispose();
                tab.View = NewView();
                tab.Page.Controls.Add(tab.View);
                tab.MessagePanel.BringToFront();
                tab.Initialization = null;
                try { await EnsureTabAsync(tab); }
                catch (Exception) { /* The new error panel supplies another retry action. */ }
            };
            buttons.Controls.Add(runtime);
            buttons.Controls.Add(retry);
            content.Controls.Add(buttons, 0, 3);
            tab.MessagePanel.Controls.Add(content);
            SetStatus(tab, "浏览器未启动；网站和账号数据未被删除。");
        }

        private void ShowOperationError(Exception error)
        {
            if (shuttingDown || IsDisposed) return;
            status.Text = "操作暂时无法完成（0x" + error.HResult.ToString("X8") + "），请重试。";
        }

        private void SetStatus(BrowserTab tab, string text)
        {
            if (!shuttingDown && !tab.Closed && CurrentTab == tab) status.Text = text;
        }

        private static string ShortTitle(string text)
        {
            string title = (text ?? "网站").Replace("\r", " ").Replace("\n", " ").Trim();
            if (title.Length == 0) title = "网站";
            return title.Length <= 20 ? title : title.Substring(0, 19) + "…";
        }

        private void ShowBrowser()
        {
            concealed = false;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Visible) Show();
            UpdateAllMedia();
            Activate();
            RefreshControls();
        }

        public void Conceal()
        {
            if (shuttingDown || IsDisposed) return;
            concealed = true;
            UpdateAllMedia();
            if (Visible) WindowState = FormWindowState.Minimized;
        }

        private void ReturnToChat()
        {
            Conceal();
            if (returnToChat != null) returnToChat();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (tabs == null || resizing || shuttingDown) return;
            resizing = true;
            try
            {
                if (WindowState == FormWindowState.Minimized) concealed = true;
                else if (Visible) concealed = false;
                UpdateAllMedia();
            }
            finally { resizing = false; }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!shuttingDown && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Conceal();
                return;
            }
            base.OnFormClosing(e);
        }

        public void Shutdown()
        {
            if (shuttingDown || IsDisposed) return;
            shuttingDown = true;
            foreach (BrowserTab tab in new List<BrowserTab>(tabStates.Values))
            {
                tab.Closed = true;
                tab.View.Dispose();
            }
            tabStates.Clear();
            Close();
            Dispose();
        }

        private void CloseTab(BrowserTab tab)
        {
            if (tab.Closed) return;
            tab.Closed = true;
            tabStates.Remove(tab.Page);
            tabs.TabPages.Remove(tab.Page);
            tab.View.Dispose();
            tab.Page.Dispose();
            RefreshControls();
            if (tabStates.Count == 0) Conceal();
        }

        private void RefreshControls()
        {
            if (shuttingDown || tabs == null || address == null) return;
            BrowserTab tab = CurrentTab;
            bool ready = tab != null && tab.Ready && !tab.Closed;
            try
            {
                back.Enabled = ready && tab.View.CoreWebView2.CanGoBack;
                forward.Enabled = ready && tab.View.CoreWebView2.CanGoForward;
                if (!address.Focused) address.Text = ready ? tab.View.CoreWebView2.Source : tab == null ? "" : tab.InitialUrl ?? "";
            }
            catch (Exception error) { ShowOperationError(error); }
        }

        private void NavigateHistory(bool ahead)
        {
            BrowserTab tab = CurrentTab;
            if (tab == null || !tab.Ready) return;
            try
            {
                if (ahead && tab.View.CoreWebView2.CanGoForward) tab.View.CoreWebView2.GoForward();
                else if (!ahead && tab.View.CoreWebView2.CanGoBack) tab.View.CoreWebView2.GoBack();
            }
            catch (Exception error) { ShowOperationError(error); }
        }

        private void ReloadCurrent()
        {
            BrowserTab tab = CurrentTab;
            if (tab == null || !tab.Ready) return;
            try { tab.View.CoreWebView2.Reload(); }
            catch (Exception error) { ShowOperationError(error); }
        }

        private async void NavigateAddress()
        {
            string normalized;
            if (!SettingsForm.TryNormalizeSiteUrl(address.Text, out normalized))
            {
                status.Text = "请输入有效的 http:// 或 https:// 网址，网址中不能包含账号密码。";
                address.Focus();
                return;
            }
            BrowserTab tab = CurrentTab;
            if (tab == null) { OpenSite(new SiteEntry { Name = new Uri(normalized).Host, Url = normalized }); return; }
            try
            {
                await EnsureTabAsync(tab);
                if (!tab.Closed && !shuttingDown) { tab.View.CoreWebView2.Navigate(normalized); tab.View.Focus(); }
            }
            catch (Exception error) { ShowOperationError(error); }
        }

        private void UpdateAllMedia()
        {
            if (tabs == null || shuttingDown) return;
            foreach (BrowserTab tab in new List<BrowserTab>(tabStates.Values)) UpdateMedia(tab, false);
        }

        private void UpdateMedia(BrowserTab tab, bool forcePause)
        {
            if (!tab.Ready || tab.Closed || shuttingDown) return;
            bool shouldMute = concealed || CurrentTab != tab;
            try
            {
                if (shouldMute)
                {
                    if (!tab.MutedByHost)
                    {
                        tab.PreviousMute = tab.View.CoreWebView2.IsMuted;
                        tab.MutedByHost = true;
                        tab.View.CoreWebView2.IsMuted = true;
                        if (tab.NavigationLoaded) RunMediaScript(tab, PauseMediaScript);
                    }
                    else if (forcePause && tab.NavigationLoaded) RunMediaScript(tab, PauseMediaScript);
                }
                else if (tab.MutedByHost)
                {
                    tab.View.CoreWebView2.IsMuted = tab.PreviousMute;
                    tab.MutedByHost = false;
                    if (tab.NavigationLoaded) RunMediaScript(tab, ResumeMediaScript);
                }
            }
            catch (Exception error) { ShowOperationError(error); }
        }

        private static async void RunMediaScript(BrowserTab tab, string script)
        {
            try
            {
                if (!tab.Closed && tab.Ready) await tab.View.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception) { /* Media may be removed or the renderer may navigate during conceal. Audio remains muted at the browser level. */ }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.L)) { address.Focus(); address.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.R)) { ReloadCurrent(); return true; }
            if (keyData == (Keys.Control | Keys.W)) { BrowserTab tab = CurrentTab; if (tab != null) CloseTab(tab); return true; }
            if (keyData == (Keys.Alt | Keys.Left)) { NavigateHistory(false); return true; }
            if (keyData == (Keys.Alt | Keys.Right)) { NavigateHistory(true); return true; }
            if (keyData == Keys.Escape && !address.Focused) { ReturnToChat(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}

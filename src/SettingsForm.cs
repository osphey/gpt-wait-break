using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GptWaitBreak
{
    public sealed class SettingsForm : Form
    {
        private readonly DataGridView sites;
        private readonly CheckBox enabled;
        private readonly CheckBox uiAutomation;
        private readonly NumericUpDown delay;
        private readonly NumericUpDown debounce;
        private readonly TextBox processNames;
        private readonly TextBox busyLabels;
        private readonly TextBox idleLabels;
        private readonly TextBox attentionLabels;
        private readonly Panel detectionFields;
        private bool updatingDefault;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            Text = "等待时刷一会 · 设置";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(850, 730);
            MinimumSize = new Size(730, 660);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 251);
            ShowInTaskbar = true;

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(22, 18, 22, 16);
            layout.ColumnCount = 1;
            layout.RowCount = 3;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            Controls.Add(layout);

            Panel heading = new Panel { Dock = DockStyle.Fill };
            Label title = new Label { Text = "把等待时间留给喜欢的网站", AutoSize = true, Location = new Point(0, 0), Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = Color.FromArgb(30, 41, 59) };
            Label subtitle = new Label { Text = "AI 回答时打开网站；结束或中断时收起网站并尝试返回聊天。", AutoSize = true, Location = new Point(1, 42), ForeColor = Color.FromArgb(83, 98, 119) };
            heading.Controls.Add(title);
            heading.Controls.Add(subtitle);
            layout.Controls.Add(heading, 0, 0);

            TabControl pages = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 7) };
            TabPage websitePage = new TabPage("网站与行为") { BackColor = Color.White, Padding = new Padding(16) };
            TabPage detectionPage = new TabPage("普通聊天检测（实验）") { BackColor = Color.White, Padding = new Padding(16) };
            pages.TabPages.Add(websitePage);
            pages.TabPages.Add(detectionPage);
            layout.Controls.Add(pages, 0, 1);

            TableLayoutPanel websiteLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            websiteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            websiteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            websiteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            websiteLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            websiteLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            websitePage.Controls.Add(websiteLayout);
            enabled = new CheckBox { Text = "启用等待时自动打开网站", Checked = settings.Enabled, AutoSize = true, Dock = DockStyle.Fill };
            websiteLayout.Controls.Add(enabled, 0, 0);
            FlowLayoutPanel timing = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
            timing.Controls.Add(new Label { Text = "回答超过", AutoSize = true, Margin = new Padding(0, 5, 7, 0) });
            delay = Number(Math.Max(0.5M, Math.Min(30M, settings.DelayMilliseconds / 1000M)), 0.5M, 30M);
            timing.Controls.Add(delay);
            timing.Controls.Add(new Label { Text = "秒，再显示网站", AutoSize = true, Margin = new Padding(7, 5, 0, 0) });
            websiteLayout.Controls.Add(timing, 0, 1);
            websiteLayout.Controls.Add(new Label { Text = "常用网站  ·  勾选一个默认网站，也可在浏览窗口手动切换", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(51, 65, 85) }, 0, 2);

            sites = new DataGridView();
            sites.Dock = DockStyle.Fill;
            sites.BackgroundColor = Color.White;
            sites.BorderStyle = BorderStyle.FixedSingle;
            sites.AutoGenerateColumns = false;
            sites.AllowUserToAddRows = true;
            sites.AllowUserToDeleteRows = true;
            sites.AllowUserToResizeRows = false;
            sites.RowHeadersVisible = false;
            sites.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            sites.MultiSelect = false;
            sites.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            sites.RowTemplate.Height = 34;
            sites.ColumnHeadersHeight = 36;
            sites.EnableHeadersVisualStyles = false;
            sites.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(241, 245, 249);
            sites.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(51, 65, 85);
            sites.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            sites.DefaultCellStyle.SelectionForeColor = Color.FromArgb(30, 41, 59);
            sites.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Default", HeaderText = "默认", Width = 58, SortMode = DataGridViewColumnSortMode.NotSortable });
            sites.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "网站名称", Width = 140, MaxInputLength = 80, SortMode = DataGridViewColumnSortMode.NotSortable });
            sites.Columns.Add(new DataGridViewTextBoxColumn { Name = "Url", HeaderText = "网址（https://…）", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MaxInputLength = 2048, SortMode = DataGridViewColumnSortMode.NotSortable });
            if (settings.Sites != null)
            {
                foreach (SiteEntry site in settings.Sites)
                {
                    if (site != null) sites.Rows.Add(String.Equals(site.Url, settings.DefaultSiteUrl, StringComparison.Ordinal), site.Name, site.Url);
                }
            }
            sites.CurrentCellDirtyStateChanged += delegate {
                if (sites.IsCurrentCellDirty && sites.CurrentCell != null && sites.CurrentCell.ColumnIndex == 0) sites.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            sites.CellValueChanged += DefaultChanged;
            sites.DataError += delegate(object sender, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            websiteLayout.Controls.Add(sites, 0, 3);
            TableLayoutPanel siteFooter = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            siteFooter.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            FlowLayoutPanel siteActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Button add = SmallButton("添加网站");
            Button remove = SmallButton("删除所选");
            add.Click += delegate {
                int index = sites.Rows.Add(false, "", "https://");
                sites.CurrentCell = sites.Rows[index].Cells[1];
                sites.BeginEdit(true);
            };
            remove.Click += delegate {
                if (sites.CurrentRow != null && !sites.CurrentRow.IsNewRow) sites.Rows.Remove(sites.CurrentRow);
            };
            siteActions.Controls.Add(add);
            siteActions.Controls.Add(remove);
            siteFooter.Controls.Add(siteActions, 0, 0);
            siteFooter.Controls.Add(new Label { Text = "登录由网站自行完成，浏览器保存本机登录态；过期或网站要求验证时需重新登录。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 116, 139), AutoEllipsis = true }, 0, 1);
            websiteLayout.Controls.Add(siteFooter, 0, 4);

            TableLayoutPanel detectorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            detectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            detectorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            detectorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detectionPage.Controls.Add(detectorLayout);
            uiAutomation = new CheckBox { Text = "启用普通聊天界面检测（UI Automation）", AutoSize = true, Checked = settings.EnableUiAutomation, Dock = DockStyle.Fill };
            detectorLayout.Controls.Add(uiAutomation, 0, 0);
            detectorLayout.Controls.Add(new Label { Text = "本地 Work / Codex 优先使用插件 hooks，无需打开此选项。\r\n实验检测仅匹配按钮名称；界面不可读或状态不明确时不会自动弹出网站。", Dock = DockStyle.Fill, ForeColor = Color.FromArgb(100, 116, 139) }, 0, 1);
            detectionFields = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Enabled = uiAutomation.Checked };
            detectorLayout.Controls.Add(detectionFields, 0, 2);
            TableLayoutPanel fields = new TableLayoutPanel { Dock = DockStyle.Top, Height = 338, ColumnCount = 3, RowCount = 5 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            detectionFields.Controls.Add(fields);
            fields.Controls.Add(new Label { Text = "目标程序名（不含路径；每行一个）", Dock = DockStyle.Fill }, 0, 0);
            fields.SetColumnSpan(fields.GetControlFromPosition(0, 0), 3);
            processNames = MultiLine(settings.ChatProcessNames);
            fields.Controls.Add(processNames, 0, 1);
            fields.SetColumnSpan(processNames, 3);
            fields.Controls.Add(new Label { Text = "生成中按钮名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            fields.Controls.Add(new Label { Text = "空闲按钮名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 1, 2);
            fields.Controls.Add(new Label { Text = "需要操作的按钮名称", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 2, 2);
            busyLabels = MultiLine(settings.BusyLabels);
            idleLabels = MultiLine(settings.IdleLabels);
            attentionLabels = MultiLine(settings.AttentionLabels);
            fields.Controls.Add(busyLabels, 0, 3);
            fields.Controls.Add(idleLabels, 1, 3);
            fields.Controls.Add(attentionLabels, 2, 3);
            FlowLayoutPanel debouncePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
            debouncePanel.Controls.Add(new Label { Text = "结束状态稳定", AutoSize = true, Margin = new Padding(0, 4, 7, 0) });
            debounce = Number(Math.Max(0.2M, Math.Min(5M, settings.StopDebounceMilliseconds / 1000M)), 0.2M, 5M);
            debouncePanel.Controls.Add(debounce);
            debouncePanel.Controls.Add(new Label { Text = "秒后返回（每行一个名称，完整匹配）", AutoSize = true, Margin = new Padding(7, 4, 0, 0) });
            fields.Controls.Add(debouncePanel, 0, 4);
            fields.SetColumnSpan(debouncePanel, 3);
            uiAutomation.CheckedChanged += delegate { detectionFields.Enabled = uiAutomation.Checked; };

            FlowLayoutPanel footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 12, 0, 0), WrapContents = false };
            Button save = new Button { Text = "保存设置", Size = new Size(112, 34), BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            save.FlatAppearance.BorderSize = 0;
            Button cancel = new Button { Text = "取消", Size = new Size(88, 34), DialogResult = DialogResult.Cancel };
            save.Click += SaveSettings;
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            layout.Controls.Add(footer, 0, 2);
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum)
        {
            return new NumericUpDown { Value = value, Minimum = minimum, Maximum = maximum, DecimalPlaces = 1, Increment = 0.1M, Width = 75, TextAlign = HorizontalAlignment.Center };
        }

        private static Button SmallButton(string text)
        {
            return new Button { Text = text, Size = new Size(95, 28), Margin = new Padding(0, 5, 8, 0), FlatStyle = FlatStyle.System };
        }

        private static TextBox MultiLine(string[] values)
        {
            return new TextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, MaxLength = 12000, Text = String.Join(Environment.NewLine, values ?? new string[0]), Margin = new Padding(0, 0, 9, 5) };
        }

        private void DefaultChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (updatingDefault || e.RowIndex < 0 || e.ColumnIndex != 0 || !Convert.ToBoolean(sites.Rows[e.RowIndex].Cells[0].Value ?? false)) return;
            updatingDefault = true;
            try
            {
                foreach (DataGridViewRow row in sites.Rows)
                    if (row.Index != e.RowIndex && !row.IsNewRow) row.Cells[0].Value = false;
            }
            finally { updatingDefault = false; }
        }

        internal static bool TryNormalizeSiteUrl(string input, out string normalized)
        {
            normalized = null;
            string value = (input ?? "").Trim();
            if (value.Length == 0 || value.Length > 2048) return false;
            if (value.IndexOf(':') < 0) value = "https://" + value;
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrEmpty(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)) return false;
            normalized = uri.AbsoluteUri;
            return true;
        }

        private static string[] Lines(TextBox text, bool processes)
        {
            List<string> values = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in text.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string value = line.Trim();
                if (processes && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value.Substring(0, value.Length - 4);
                if (value.Length == 0) continue;
                if (value.Length > 160 || (processes && value.IndexOfAny(new[] { '\\', '/' }) >= 0)) throw new ArgumentException("程序名不应包含路径；按钮名称或程序名最长为 160 个字符。");
                if (seen.Add(value)) values.Add(value);
                if (values.Count > 100) throw new ArgumentException("每组最多填写 100 个名称。");
            }
            return values.ToArray();
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            sites.EndEdit();
            List<SiteEntry> entries = new List<SiteEntry>();
            HashSet<string> urls = new HashSet<string>(StringComparer.Ordinal);
            string defaultUrl = null;
            foreach (DataGridViewRow row in sites.Rows)
            {
                if (row.IsNewRow) continue;
                string name = Convert.ToString(row.Cells[1].Value).Trim();
                string url = Convert.ToString(row.Cells[2].Value).Trim();
                if (name.Length == 0 && url.Length == 0) continue;
                string normalized;
                if (name.Length == 0 || !TryNormalizeSiteUrl(url, out normalized))
                {
                    sites.CurrentCell = row.Cells[name.Length == 0 ? 1 : 2];
                    MessageBox.Show(this, "请填写网站名称及有效的 http:// 或 https:// 网址。网址中不能包含账号密码。", "检查网站设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!urls.Add(normalized))
                {
                    sites.CurrentCell = row.Cells[2];
                    MessageBox.Show(this, "列表中已有相同的网址，请删除重复项。", "检查网站设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                entries.Add(new SiteEntry { Name = name, Url = normalized });
                if (Convert.ToBoolean(row.Cells[0].Value ?? false)) defaultUrl = normalized;
                if (entries.Count > 50)
                {
                    MessageBox.Show(this, "最多添加 50 个常用网站。", "检查网站设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            if (entries.Count == 0)
            {
                MessageBox.Show(this, "请至少添加一个常用网站。", "检查网站设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                string[] processes = Lines(processNames, true);
                string[] busy = Lines(busyLabels, false);
                string[] idle = Lines(idleLabels, false);
                string[] attention = Lines(attentionLabels, false);
                if (processes.Length == 0 || busy.Length == 0 || idle.Length == 0 || attention.Length == 0) throw new ArgumentException("请保留目标程序名及三组按钮名称；每组至少填写一项。关闭实验检测后不会使用这些名称。");
                Result = new AppSettings {
                    Enabled = enabled.Checked,
                    DelayMilliseconds = Decimal.ToInt32(delay.Value * 1000M),
                    StopDebounceMilliseconds = Decimal.ToInt32(debounce.Value * 1000M),
                    EnableUiAutomation = uiAutomation.Checked,
                    ChatProcessNames = processes, BusyLabels = busy, IdleLabels = idle, AttentionLabels = attention,
                    DefaultSiteUrl = defaultUrl ?? entries[0].Url,
                    Sites = entries
                };
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (ArgumentException error)
            {
                MessageBox.Show(this, error.Message, "检查检测设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}

using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace GptWaitBreak
{
    public static class Configuration
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 20 };

        public static AppSettings Load()
        {
            if (!File.Exists(AppPaths.ConfigFile)) return AppSettings.CreateDefault();
            if (new FileInfo(AppPaths.ConfigFile).Length > 65536) throw new InvalidDataException("设置文件过大。请修复 settings.json。");
            AppSettings settings = Json.Deserialize<AppSettings>(File.ReadAllText(AppPaths.ConfigFile, Encoding.UTF8));
            Validate(settings);
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            Validate(settings);
            Directory.CreateDirectory(AppPaths.DataDirectory);
            string temp = AppPaths.ConfigFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temp, Json.Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(AppPaths.ConfigFile)) File.Replace(temp, AppPaths.ConfigFile, null);
                else File.Move(temp, AppPaths.ConfigFile);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        public static bool IsWebUrl(string value)
        {
            Uri uri;
            return !String.IsNullOrWhiteSpace(value) && value.Length <= 4096
                && Uri.TryCreate(value, UriKind.Absolute, out uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && !String.IsNullOrEmpty(uri.Host) && String.IsNullOrEmpty(uri.UserInfo);
        }

        public static void Validate(AppSettings settings)
        {
            if (settings == null) throw new InvalidDataException("设置内容为空。");
            if (settings.DelayMilliseconds < 0 || settings.DelayMilliseconds > 60000) throw new InvalidDataException("等待延迟必须为 0–60 秒。");
            if (settings.StopDebounceMilliseconds < 100 || settings.StopDebounceMilliseconds > 10000) throw new InvalidDataException("结束确认延迟必须为 0.1–10 秒。");
            if (settings.Sites == null || settings.Sites.Count == 0 || settings.Sites.Count > 50) throw new InvalidDataException("请配置 1–50 个网站。");
            bool found = false;
            foreach (SiteEntry site in settings.Sites) {
                if (site == null || String.IsNullOrWhiteSpace(site.Name) || site.Name.Length > 100 || !IsWebUrl(site.Url))
                    throw new InvalidDataException("网站须有名称和有效的 HTTP(S) 网址，且网址不包含账号密码。");
                if (site.Url == settings.DefaultSiteUrl) found = true;
            }
            if (!found) throw new InvalidDataException("默认网站必须在网站列表内。");
            ValidateLabels(settings.ChatProcessNames, "进程名称");
            ValidateLabels(settings.BusyLabels, "生成按钮名称");
            ValidateLabels(settings.IdleLabels, "空闲按钮名称");
            ValidateLabels(settings.AttentionLabels, "需操作按钮名称");
        }

        private static void ValidateLabels(string[] labels, string name)
        {
            if (labels == null || labels.Length == 0 || labels.Length > 100) throw new InvalidDataException(name + "必须有 1–100 项。");
            foreach (string label in labels) if (String.IsNullOrWhiteSpace(label) || label.Length > 200)
                throw new InvalidDataException(name + "包含空值或过长的内容。");
        }

        public static SiteEntry DefaultSite(AppSettings settings)
        {
            foreach (SiteEntry site in settings.Sites) if (site.Url == settings.DefaultSiteUrl) return site;
            return settings.Sites[0];
        }
    }
}

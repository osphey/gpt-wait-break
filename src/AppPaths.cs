using System;
using System.IO;
using System.Security.Principal;

namespace GptWaitBreak
{
    public static class AppPaths
    {
        public static readonly string DataDirectory = ResolveDataDirectory();
        public static string BrowserProfile { get { return Path.Combine(DataDirectory, "BrowserProfile"); } }
        public static string ConfigFile { get { return Path.Combine(DataDirectory, "settings.json"); } }
        public static string Executable { get { return System.Reflection.Assembly.GetExecutingAssembly().Location; } }
        public static string PipeName { get {
            string name = "GptWaitBreak-" + WindowsIdentity.GetCurrent().User.Value.Replace('-', '_');
            if (!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GPT_WAIT_BREAK_DATA_DIR"))) {
                using (var hash = System.Security.Cryptography.SHA256.Create()) {
                    name += "-test-" + BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant()))).Replace("-", "").Substring(0, 16);
                }
            }
            return name;
        } }
        public static string MutexName { get { return "Local\\" + PipeName; } }

        private static string ResolveDataDirectory()
        {
            string custom = Environment.GetEnvironmentVariable("GPT_WAIT_BREAK_DATA_DIR");
            return String.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GptWaitBreak")
                : Path.GetFullPath(custom);
        }
    }
}

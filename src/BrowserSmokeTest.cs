using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace GptWaitBreak
{
    // Integration test only: refuses to run against the normal user's browsing profile.
    internal static class BrowserSmokeTest
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2 || String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GPT_WAIT_BREAK_DATA_DIR"))) return 2;
            string reportPath = Path.GetFullPath(args[1]);
            bool readCookie = args.Length > 2 && args[2] == "--read-cookie";
            int result = 1;
            using (LocalTestServer server = new LocalTestServer())
            using (BrowserWindow browser = new BrowserWindow(AppSettings.CreateDefault(), delegate { }, delegate { })) {
                browser.Shown += async delegate {
                    try {
                        SiteEntry site = new SiteEntry { Name = "本地集成测试", Url = server.Url };
                        browser.OpenSite(site);
                        WebView2 view = null;
                        for (int i = 0; i < 80; i++) {
                            view = browser.WebViewForTesting;
                            if (view != null && view.CoreWebView2 != null && view.CoreWebView2.Source.StartsWith(server.Url, StringComparison.Ordinal)) {
                                string ready = await view.CoreWebView2.ExecuteScriptAsync("document.readyState");
                                if (ready == "\"complete\"") break;
                            }
                            await Task.Delay(250);
                        }
                        if (view == null || view.CoreWebView2 == null || !view.CoreWebView2.Source.StartsWith(server.Url, StringComparison.Ordinal))
                            throw new Exception("WebView2 did not initialize/navigate within 20 seconds.");
                        bool cookiePersisted = false;
                        var cookies = await view.CoreWebView2.CookieManager.GetCookiesAsync(server.Url);
                        foreach (var cookie in cookies) if (cookie.Name == "gpt_wait_break_fixture" && cookie.Value == "persistent") cookiePersisted = true;
                        if (readCookie && !cookiePersisted) throw new Exception("Persistent fixture cookie was not restored in second process.");
                        if (!readCookie) {
                            var cookie = view.CoreWebView2.CookieManager.CreateCookie("gpt_wait_break_fixture", "persistent", "127.0.0.1", "/");
                            cookie.Expires = DateTime.Now.AddDays(1);
                            cookie.IsHttpOnly = true;
                            view.CoreWebView2.CookieManager.AddOrUpdateCookie(cookie);
                            await Task.Delay(300);
                        }
                        await view.CoreWebView2.ExecuteScriptAsync("window.scrollTo(0,420);window.__waitBreakFixture='same-document';");
                        int originalTabCount = browser.TabCountForTesting;
                        await view.CoreWebView2.ExecuteScriptAsync("window.open('/popup','wait-break-popup-fixture');");
                        WebView2 popup = null;
                        for (int i = 0; i < 60; i++) {
                            WebView2 candidate = browser.WebViewForTesting;
                            if (browser.TabCountForTesting == originalTabCount + 1 && candidate != view && candidate != null && candidate.CoreWebView2 != null) {
                                string ready = await candidate.CoreWebView2.ExecuteScriptAsync("document.readyState");
                                if (ready == "\"complete\"" && candidate.CoreWebView2.Source.Contains("/popup")) { popup = candidate; break; }
                            }
                            await Task.Delay(100);
                        }
                        if (popup == null) throw new Exception("Same-environment popup did not initialize.");
                        string opener = await popup.CoreWebView2.ExecuteScriptAsync("window.opener && window.opener.__waitBreakFixture");
                        if (opener != "\"same-document\"") throw new Exception("Popup lost the opener needed by login flows.");
                        var popupCookies = await popup.CoreWebView2.CookieManager.GetCookiesAsync(server.Url);
                        bool sharedCookies = false;
                        foreach (var cookie in popupCookies) if (cookie.Name == "gpt_wait_break_fixture") sharedCookies = true;
                        if (!sharedCookies) throw new Exception("Popup did not share the persistent browser profile.");
                        await popup.CoreWebView2.ExecuteScriptAsync("window.opener.__waitBreakPopupReturned=true;window.close();");
                        for (int i = 0; i < 30 && browser.TabCountForTesting != originalTabCount; i++) await Task.Delay(100);
                        if (browser.TabCountForTesting != originalTabCount || await view.CoreWebView2.ExecuteScriptAsync("window.__waitBreakPopupReturned") != "true")
                            throw new Exception("Popup close did not return to the opener.");
                        browser.Conceal();
                        await Task.Delay(200);
                        bool minimized = browser.WindowState == FormWindowState.Minimized;
                        bool muted = view.CoreWebView2.IsMuted;
                        if (!minimized || !muted) throw new Exception("Conceal did not minimize/mute the browser.");
                        browser.OpenSite(site);
                        await Task.Delay(200);
                        bool reused = Object.ReferenceEquals(view, browser.WebViewForTesting) && originalTabCount == browser.TabCountForTesting;
                        string sentinel = await view.CoreWebView2.ExecuteScriptAsync("window.__waitBreakFixture");
                        if (!reused || sentinel != "\"same-document\"") throw new Exception("Reopening site replaced the existing document.");
                        if (view.CoreWebView2.IsMuted) throw new Exception("Reopening did not restore the prior unmuted state.");
                        string title = await view.CoreWebView2.ExecuteScriptAsync("document.title");
                        File.WriteAllText(reportPath, Configuration.Json.Serialize(new {
                            passed = true, stage = readCookie ? "persistent-profile-reopen" : "initial-profile",
                            sameTabReused = reused, minimized = minimized, hiddenMediaMuted = muted,
                            popupOpenerPreserved = true, popupProfileShared = sharedCookies, popupCloseReturned = true,
                            cookiePersisted = cookiePersisted, pageTitle = title,
                            note = "Only an isolated localhost fixture was tested; no social-media account was accessed."
                        }), new UTF8Encoding(false));
                        result = 0;
                    } catch (Exception error) {
                        File.WriteAllText(reportPath, Configuration.Json.Serialize(new { passed = false, error = error.Message }), new UTF8Encoding(false));
                    } finally { browser.Shutdown(); Application.ExitThread(); }
                };
                Application.Run(browser);
            }
            return result;
        }

        private sealed class LocalTestServer : IDisposable
        {
            private readonly TcpListener listener;
            private volatile bool stopped;
            public string Url { get; private set; }
            public LocalTestServer()
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
                Thread serverThread = new Thread(Serve) { IsBackground = true, Name = "WaitBreak localhost fixture" };
                serverThread.Start();
            }
            private void Serve()
            {
                while (!stopped) {
                    try {
                        using (TcpClient client = listener.AcceptTcpClient()) {
                            client.ReceiveTimeout = 1500;
                            using (NetworkStream stream = client.GetStream()) {
                                StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                                string line;
                                int lines = 0;
                                do { line = reader.ReadLine(); } while (!String.IsNullOrEmpty(line) && ++lines < 100);
                                byte[] body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>Wait Break Test</title><style>body{font:20px system-ui;padding:70px;background:#f1f5f9;color:#0f172a;height:2400px}section{background:white;padding:40px;border-radius:20px;max-width:750px}p{color:#475569}</style><section><h1>GPT Wait Break</h1><p>本地集成测试：网页复用、收起静音与登录态持久化。</p><p>此页面未访问任何社媒账号。</p></section>");
                                byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
                                stream.Write(header, 0, header.Length); stream.Write(body, 0, body.Length); stream.Flush();
                            }
                        }
                    } catch (SocketException) { if (stopped) return; }
                      catch (IOException) { }
                }
            }
            public void Dispose() { stopped = true; listener.Stop(); }
        }
    }
}

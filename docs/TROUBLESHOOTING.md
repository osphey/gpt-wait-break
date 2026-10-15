# 故障排查

## 插件未出现或 hooks 未执行

确认下载的是带 `bin/` 的 Release，完整安装了 marketplace，并在桌面插件目录启用了 GPT Wait Break。安装后的副本需要同时含 `bin/GptWaitBreak.exe` 和 `hooks/hooks.json`。

```powershell
codex plugin marketplace list
codex plugin list
```

重启桌面端后，按官方 hooks 审核流程信任当前定义。安装或启用插件本身不等于信任 hooks。不要手动改动信任数据库。企业设备可能仅允许受管理 hooks，个人插件无法覆盖这一限制。[官方 Hooks 文档](https://learn.chatgpt.com/docs/hooks)

这个包针对 Windows 上本地编排的 Work/Codex。云端 Work 即使能执行本地工具，也不一定执行本地插件命令 hooks。普通聊天不会通过这些 hooks 触发，需要单独启用实验检测。

## 程序无法启动或网站窗口空白

先检查 Windows x64、.NET Framework 4.8 和 [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。确认 `bin/` 中的两个 WebView2 托管 DLL 与 x64 `WebView2Loader.dll` 都在，不要单独搬走 EXE。

用托盘“保存诊断报告”，或显式写入文件：

```powershell
.\bin\GptWaitBreak.exe --diagnose .\diagnostics.json
```

报告写入是短时进程，文件可能稍后出现。运行时显示 `unavailable` 时，先修复 WebView2 Runtime。不要禁用 WebView2 的正常安全设置来绕过网站登录限制。

## 发送消息没有弹出网站

- 检查托盘“启用等待浏览”是否勾选，以及回答是否在延迟时间内完成。
- 开始事件只在目标 GPT/Codex 窗口可确认时接入；如果从 CLI、后台窗口或不支持的客户端提交，可能无法取得返回目标。
- 延迟到达时，如果你已转到其他应用，程序会等待而不抢到前台。
- 普通聊天需先保持托盘程序运行，再手动启用实验 UI Automation。控件名称不匹配、不可见或接口没有暴露状态时会保持 Unknown。
- `UserPromptSubmit`/其他事件缺少 session/turn 标识时，程序选择忽略，不冒险把不同任务混在一起。

## 网站没有自动收起或没有聚焦 GPT

先用网站“返回聊天”或托盘菜单手动返回。普通聊天被遮挡或最小化后可能失去可读控件，程序不会因停止按钮消失就假定完成。

如果已经离开网站转到其他应用，结束事件只闪烁 GPT 任务栏图标。Windows 也可能拒绝后台程序设为前台；应手动点回原窗口。关闭原 GPT 窗口或同时切换多个聊天页时，无法保证回到某个具体会话。

## 登录不能保存或网站拒绝登录

这里是独立 WebView2 浏览器环境，需要首次自行登录，不能自动继承 Chrome/Edge/GPT 浏览器的账号。确认 `%LOCALAPPDATA%\GptWaitBreak\BrowserProfile` 可写且没有被清理工具删除。网站会话过期、风控或嵌入式浏览器限制无法由插件保证解决。

如站点必须在系统浏览器登录，可用常规浏览器访问它；本版本无法自动控制那一个浏览器窗口的生命周期。请勿上传 BrowserProfile 或 Cookie 来排查。

## 更新或卸载失败

先在托盘“退出”。如果目标目录缺少有效安装 receipt，脚本不会递归删除或覆盖它；核对目录归属后再处理，不要把卸载器改成删除任意路径。

`Uninstall.ps1 -RemovePlugin` 的 CLI 移除失败时，先到桌面插件目录移除插件，再不带该开关执行卸载。默认设置和登录数据被保留，需要明确传 `-RemoveData` 才删除。

## 提交 issue

可以提供 Windows/桌面客户端版本、使用 Chat/本地 Work/Codex 哪种模式、复现步骤、隐去个人路径的诊断报告。不要提交聊天原文、社媒密码、验证码、Cookie、BrowserProfile 或含账号令牌的网址。

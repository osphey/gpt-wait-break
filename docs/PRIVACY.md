# 隐私和本机数据

GPT Wait Break 没有自建服务、遥测上传或模型 API 请求。你打开的网站、WebView2 Runtime 自身及 GPT/Codex 客户端仍遵循各自的网络与隐私行为。

## 存储位置

| 数据 | 默认位置 | 用途 |
| --- | --- | --- |
| 自定义网址与设置 | `%LOCALAPPDATA%\GptWaitBreak\settings.json` | 网站列表、延迟、启用状态、实验检测按钮名称 |
| 浏览器配置 | `%LOCALAPPDATA%\GptWaitBreak\BrowserProfile` | Cookie、网站本地存储、缓存等 WebView2 数据 |
| 应用文件 | `%LOCALAPPDATA%\Programs\GptWaitBreak` | 程序、依赖、安装 receipt、插件定义和文档 |
| 插件缓存与启用配置 | 由 Codex 客户端管理 | 本地 marketplace 的安装副本、hooks 信任状态 |

同一 Windows 用户的所有本插件实例复用同一数据目录和单例伴侣。更新或重新安装插件默认保留数据。浏览器配置应视为敏感信息，不应提交 Git、上传 issue 或随发行包分发。

## hooks 收到什么

GPT/Codex 会向 hook 的标准输入发送原始事件 JSON，依事件可能包含 prompt、回答文字、工具输入或路径。本程序会在本机内存中解析输入，**仅选取事件类型、session/turn 标识**，再补充目标窗口句柄和进程 ID，用于向同用户的本地命名管道转发。

程序不把 prompt、回答文字、工具输入、transcript 路径或 Cookie 写入日志，不向网站注入这些字段；hook 标准输出为无内容指令的 `{}`。本地 IPC 按当前 Windows 用户 SID 设置访问权限，不监听网络端口。

## 实验性无障碍检测

只查询目标窗口暴露的按钮名称、控件类型和可用/可见状态，以判断 Busy、Idle、NeedsUser 或 Unknown。检测器不读取消息气泡、输入框内容或聊天记录，也不保存界面截图。默认关闭，需要用户显式启用。

## 网站账号

你直接在网站页面登录，账号信息由网站与 WebView2 处理。本程序不收集一份社媒密码，也不会请求你在 GPT 聊天中提交验证码、密码或 Cookie。浏览器配置与 Chrome、Edge 和 GPT 内置浏览器隔离。

普通网站导航、登录跳转和网页脚本按 WebView2 正常行为运行，不能保证每个站点都接受该浏览器环境。不要把本程序当作隐身浏览器或账号隔离沙箱。

## 诊断与删除

托盘“保存诊断报告”由你主动保存 JSON，包含版本、WebView2 运行时、配置是否有效、实验检测是否开启和数据目录路径，不含聊天内容或网站会话。共享前仍可检查其中的本机用户名/路径。

卸载默认保留数据。明确需要清除时，在退出程序后运行 `Uninstall.ps1 -RemoveData`；如果用了 CLI 安装插件，可同时传 `-RemovePlugin`。删除本机数据不等于在网站服务器上撤销所有登录，必要时在网站的账号安全页面退出会话。

开发测试可设置 `GPT_WAIT_BREAK_DATA_DIR` 指向隔离数据目录；正式使用一般无需更改。自定义目录的数据不由默认卸载器删除。

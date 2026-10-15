# GPT Wait Break

AI 思考时刷一会，回答完成或中断后回到聊天。

一个 Windows 本地 Work/Codex 插件：只包含 hooks、skill 和短时 PowerShell 脚本，无需额外程序。
网站在已有 **Edge / Chrome 的独立窗口**打开，不是 GPT 内置浏览器。

## 功能

- 默认等待 2 秒再打开网站，短回答不打断。
- 回答结束、中断或需要批准操作时，收起网站并尝试聚焦 GPT。
- 用自然语言设置网址、延迟、浏览器和开关。
- 专用浏览器配置保留本机登录态，不影响日常浏览器窗口。

## 安装

1. 从 [Releases](https://github.com/osphey/gpt-wait-break/releases/tag/v0.2.2) 下载 `gpt-wait-break-0.2.2-plugin.zip` 并解压。
2. 在解压目录运行 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1 -ReviewHooks`，或按[手动安装](docs/INSTALL.md)注册本地 marketplace。
3. 在新命令窗口按启动提示审核此插件的 hooks，再重启桌面端。已进入命令主界面时可输入 `/hooks`；不要在 GPT 聊天框输入。

需要 Windows、Edge 或 Chrome。普通 Chat 与云端 Work 不支持。

## 使用

安装后直接告诉 GPT：

> 把 GPT Wait Break 的网站设为 B站，等待 2 秒后打开。

> 改成 https://www.xiaohongshu.com/explore ，用 Chrome 打开。

> 暂停 GPT Wait Break。

首次在打开的网站里自行登录；登录有效期由网站决定。详见[使用方法](docs/USAGE.md)。

## 开发

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./tests/run.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/package.ps1
```

执行策略参数仅作用于本次脚本进程，不修改系统设置，不替代 hooks 信任审核。
v0.2.2 预览版。验证状态见 [CHANGELOG](CHANGELOG.md)。[MIT](LICENSE) © Osphey

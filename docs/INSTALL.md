# 安装

需要 Windows、Edge 或 Chrome，以及支持本地 hooks 的桌面客户端。

下载 [Release](https://github.com/osphey/gpt-wait-break/releases) 的插件 ZIP，解压后保留目录，在目录内运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1 -ReviewHooks
```

脚本会安装插件，并打开已有 Codex 工具的审核窗口，不修改 PATH。也可在 `codex` 命令可用时手动安装：

```powershell
codex plugin marketplace add .
codex plugin add gpt-wait-break@gpt-wait-break-local
```

在新命令窗口按启动提示查看并信任此插件的 hooks，再重启桌面端。已进入命令主界面时可输入 `/hooks`；不要在 GPT 聊天框输入。自动发现失败时可给安装脚本传 `-CodexPath` 指定已有工具，或把解压目录作为本地项目打开，在插件目录选择其 repo marketplace 安装。[官方安装说明](https://developers.openai.com/plugins/build/plugins)

脚本执行策略参数只影响本次 PowerShell 进程，不修改持久设置；组织管理策略仍优先，hooks 仍须用户审核。

## 更新与卸载

更新：将新版解压到空目录，再运行安装命令。安装器会迁移同名本地源，保留旧文件；重启桌面端并复核 hooks。

卸载：在桌面插件目录移除，或运行：

```powershell
codex plugin remove gpt-wait-break@gpt-wait-break-local
codex plugin marketplace remove gpt-wait-break-local
```

卸载插件不会删除浏览器登录数据。[Hooks 信任说明](https://learn.chatgpt.com/docs/hooks)

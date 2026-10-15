# 安装

需要 Windows、Edge 或 Chrome，以及支持本地 hooks 的桌面客户端。

下载 [Release](https://github.com/osphey/gpt-wait-break/releases) 的插件 ZIP，解压后保留目录，在目录内运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./Install.ps1
```

脚本仅注册本地 marketplace 和安装插件。也可以直接运行官方 CLI：

```powershell
codex plugin marketplace add .
codex plugin add gpt-wait-break@gpt-wait-break-local
```

重启桌面端，启用插件，按官方流程审核并信任当前 hooks。没有 CLI 时，可把解压目录作为本地项目打开，在插件目录选择其 repo marketplace 安装。[官方安装说明](https://developers.openai.com/plugins/build/plugins)

脚本执行策略参数只影响本次 PowerShell 进程，不修改持久设置；组织管理策略仍优先，hooks 仍须用户审核。

## 更新与卸载

更新：用新版替换解压目录，重新执行安装命令，重启桌面端并复核 hooks。

卸载：在桌面插件目录移除，或运行：

```powershell
codex plugin remove gpt-wait-break@gpt-wait-break-local
codex plugin marketplace remove gpt-wait-break-local
```

卸载插件不会删除浏览器登录数据。[Hooks 信任说明](https://learn.chatgpt.com/docs/hooks)

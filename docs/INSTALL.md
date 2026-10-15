# 安装、更新和卸载

## 安装前

本包仅面向 Windows 10/11 x64。请安装 .NET Framework 4.8 和 [WebView2 Evergreen Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。这些是本地程序依赖，不需要 OpenAI API key。浏览器 SDK 已包含在发行 ZIP 中，Evergreen Runtime 没有打包进来。

从 Release 下载 ZIP 与对应 `.sha256` 文件，可以用下列命令核对下载的 SHA256：

```powershell
Get-FileHash -LiteralPath .\gpt-wait-break-0.1.0-win-x64.zip -Algorithm SHA256
```

源码包没有 `bin/GptWaitBreak.exe`，须先运行 `scripts/build.ps1`。程序未做商业代码签名，设备或组织的应用策略可能限制运行；遵守本机策略。

## 安装应用和本地插件

解压后进入含有 `Install.ps1` 的目录。先检查脚本内容，再执行：

```powershell
.\Install.ps1 -RegisterPlugin -Start
```

脚本把文件复制到 `%LOCALAPPDATA%\Programs\GptWaitBreak`，创建开始菜单快捷方式，调用官方 CLI 添加本地源并安装插件，最后打开设置。它不会自动信任 hooks、修改系统安全策略或自动登录社媒。

`-RegisterPlugin` 需要 PATH 中存在 `codex`。等价的手动命令如下：

```powershell
codex plugin marketplace add "$env:LOCALAPPDATA\Programs\GptWaitBreak"
codex plugin add gpt-wait-break@gpt-wait-break-local
```

**安装插件后，重启桌面端并审核/信任 hooks。** 当前 hook 定义变化后可能需要重新审核。如果组织只允许受管理 hooks，个人插件无法绕过该策略。[官方 hooks 信任说明](https://learn.chatgpt.com/docs/hooks)

如果你的客户端支持通过桌面插件目录安装本地源，也可先执行 `.\Install.ps1 -Start`，然后使用已注册的本地 marketplace 安装插件。不同版本的本地源界面可能不同；官方文档明确支持 repo/personal marketplace 与 `codex plugin marketplace add`。[官方安装与 marketplace 文档](https://developers.openai.com/plugins/build/plugins)

## 不使用 CLI 的 repo marketplace

项目已经包含 `.agents/plugins/marketplace.json`。可以保留完整解压目录，在桌面端将其作为本地项目打开，重启客户端后到插件目录选择 **GPT Wait Break 本地插件** 源并安装。`source.path` 为 `./`，相对 marketplace 根目录指向本包。

这里加载的是插件的缓存副本。不要把二进制从包中单独移走；hook 中的 `PLUGIN_ROOT` 指向已安装插件目录，需要存在 `bin/GptWaitBreak.exe`。仅复制 skill 不能启用事件自动化。

如果客户端既没有读取 repo 本地 marketplace，也没有 CLI 注册能力，则不能在该版本完成插件安装；仍可单独使用程序和手动操作。不要通过修改 hooks 信任存储来强行安装。

## 单独运行与开机启动

```powershell
.\bin\GptWaitBreak.exe --configure
.\bin\GptWaitBreak.exe --run
```

从已安装目录运行也可以。`--configure` 打开设置；`--run` 启动托盘伴侣。可选开机启动由用户显式开启：

```powershell
.\Install.ps1 -Startup
```

该开关只创建当前用户的启动文件夹快捷方式，无需管理员权限。实验性普通聊天检测需要程序持续运行，并在设置中自行启用。

## 更新

1. 在托盘菜单退出 GPT Wait Break。
2. 下载并检查新版 Release，解压后重新运行 `Install.ps1`。
3. 使用 `-RegisterPlugin` 刷新插件安装，重启桌面端，确认更新后的 hooks 已按官方流程审核。
4. `%LOCALAPPDATA%\GptWaitBreak` 内的设置和登录配置默认保留。网页是否保持登录由网站决定。

本地安装 receipt 用于确认目录归属。若目标目录有无关文件或 receipt 不匹配，安装器会停止，避免覆盖其他内容。

## 卸载

先从托盘退出，再运行安装目录中的：

```powershell
.\Uninstall.ps1 -RemovePlugin
```

这会通过 CLI 移除插件和本地源，并删除应用及快捷方式；默认保留设置与浏览器登录配置。如果已在桌面端手动移除插件，可以不传 `-RemovePlugin`。

需要同时永久删除本机设置、Cookie 与浏览器存储时：

```powershell
.\Uninstall.ps1 -RemovePlugin -RemoveData
```

可先加 `-WhatIf` 查看操作。删除数据不会替你在网站服务器上撤销所有会话；需要时在对应网站的安全设置中处理。

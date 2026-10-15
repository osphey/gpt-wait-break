# Changelog

## 0.2.2

- 改用 `.codex-plugin/plugin.json` 兼容格式，修复当前客户端能安装却跳过插件 hooks 的问题。
- 明确 `/hooks` 在 Codex 命令窗口中输入。
## 0.2.1

- 修复系统 PowerShell 中找不到 Codex CLI 的安装错误，自动发现桌面端已有工具。
- 支持 `Install.ps1 -CodexPath <path>`；无需修改 PATH 或额外安装 CLI。
- 验证：5 项 Windows PowerShell 5 安装路径检查通过。
## 0.2.0

- 改为纯插件：hooks + skill + PowerShell，使用已有 Edge/Chrome。
- 自然语言设置网站、延迟、浏览器和开关。
- 移除配套程序、WebView2、托盘和普通聊天检测。
- 验证：状态、浏览器模块及 hook 集成自动测试通过；真实桌面客户端与网站登录需实机验收。

## 0.1.0

- 初始 Windows 伴侣原型，已由 0.2.0 替代。

# 使用方法

启用插件后，用自然语言设置：

> 把等待时的网站设为 B站，延迟 2 秒，用 Edge 打开。

> 改成 https://www.xiaohongshu.com/explore 。

> 暂停 GPT Wait Break。 / 重新启用 GPT Wait Break。

也可在插件目录直接配置：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/config.ps1 -Url 'https://www.bilibili.com/' -DelaySeconds 2 -Browser auto
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command '& ./scripts/config.ps1 -Enabled $false'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ./scripts/config.ps1
```

`Browser` 可选 `auto`、`edge`、`chrome`；只传需要改变的参数，其余设置保留。无参数显示当前设置，网址支持 HTTP/HTTPS。

发送消息后，插件等待指定时间再显示网站。回答准备结束、主动中断或需要批准操作时收起窗口；Windows 拒绝自动聚焦时，请手动点回 GPT。

网站使用 Edge/Chrome 的专用配置。首次自行登录，之后复用本机 Cookie；不会导入你日常浏览器的登录，网站仍可要求重新验证。最小化窗口不保证暂停音视频。

仅支持本地 Work/Codex。若无响应，确认插件已启用、hooks 已审核信任，且浏览器已安装。安装或更新见 [INSTALL](INSTALL.md)。

# 本地数据

插件没有服务端，不使用 OpenAI API key。

数据保存在 `%LOCALAPPDATA%\GptWaitBreakPlugin`：

- `settings.json`：网站、延迟、浏览器和开关。
- `state.json`：会话/轮次 ID、时间戳、窗口句柄和进程 ID。
- `BrowserProfile/`：浏览器保存的 Cookie、站点存储及浏览数据。

插件不保存提示词、回答正文、工具参数或社媒密码。首次登录由你在网站页面完成；登录有效期由网站决定。专用配置不会继承日常浏览器或 GPT 内置浏览器的账号。

卸载插件默认保留这些数据。要删除本机数据，先关闭专用浏览器窗口，再自行删除上述目录。
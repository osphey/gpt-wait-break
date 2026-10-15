# 架构与开发

## 组成

```text
本地 Work/Codex 生命周期 hooks
    → 短时 --hook 进程：筛选原始事件 JSON
    → 按当前用户保护的命名管道
    → 单例托盘伴侣 + 纯状态机
    → WebView2 网站窗口 / 窗口聚焦

普通 ChatGPT（显式开启实验模式）
    → UI Automation 按钮状态
    → 同一状态机和网站窗口
```

skill 只提供安装、配置与排查指引；自动等待和返回不依赖模型每轮选择调用 skill。没有 MCP server、OpenAI API key 或附加模型请求。网站使用独立顶层 WebView2 页面，避免把任意社媒嵌入 GPT 插件 iframe 的限制。

## 事件与状态

`UserPromptSubmit` 转为 start，`Stop` 转为 stop，`Interrupt`/`PermissionRequest`/`SessionEnd` 使对应等待结束。事件按 source/session/turn 关联，记录已结束事件，防止迟到的 start 再次弹出。短回答在显示前结束时不会打开网站。

`Stop` 表示一次准备停止，其他 hooks 可以请求续跑。程序以短暂去抖和后续 start 处理常见续跑，但不假设它是严格最终完成信号。UI Automation 则要求明确可用的按钮状态，Busy 优先；不能把“没有 Stop”当成 Idle。不可读状态为 Unknown。

网页只在目标聊天仍在前台，或伴侣网站正位于前台时自动显示。结束后仅在用户仍在伴侣网站时尝试聚焦 GPT；用户已切去其他应用则闪烁任务栏提示。窗口句柄和 PID 均会复核，已关闭的目标取消等待。

## 构建

仅使用 Windows x64 .NET Framework 4.8 / C# 5，编译器位于：

```text
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
```

```powershell
.\scripts\build.ps1
.\tests\run.ps1 -SkipBuild
.\scripts\package.ps1 -SkipBuild
```

SDK 固定为 Microsoft.Web.WebView2 `1.0.4258.31`；锁文件 `scripts/dependencies.json` 声明下载 URL、SHA256、net462 和 x64。每次构建验证 NuGet 包哈希，然后只解出所需 DLL 与原始许可证/通知文件。已缓存时支持 `-Offline`。

`package.ps1` 默认先构建，再运行默认测试，打包程序、插件定义、hooks、skill、安装/卸载脚本、源码、测试与文档。`-SkipBuild` 只跳过编译，不跳过测试。它会生成版本化 ZIP 和 SHA256 校验文件。运行时浏览器数据、缓存、二进制构建产物和测试报告都不提交 Git。

## 测试和实际验收

默认测试应覆盖状态机、乱序/重复/不同会话事件、配置与 URL 校验、按钮状态分类、插件 manifest 和 hook 协议。运行：

```powershell
.\tests\run.ps1 -SkipBuild
```

可选 WebView2 集成测试使用本机测试页面和隔离临时 profile；需要本机 WebView2 Runtime，可能需要交互式 Windows 桌面：

```powershell
.\tests\run.ps1 -SkipBuild -Browser
```

集成测试不会登录真实社媒、读取实际 GPT 聊天界面或改动用户插件配置。自动构建模板位于 [windows-build-workflow.yml.example](windows-build-workflow.yml.example)；仓库当前没有启用 Actions。需要时由具有 workflow 权限的维护者将它放到 `.github/workflows/windows.yml`，即可编译、运行非浏览器测试，并在版本标签上发布安装包。GitHub Actions 的无交互会话不作为真实桌面焦点行为的验收。

发布前仍应由使用者在实际客户端验收：本地插件发现/安装/信任、较长回答、短回答、中断、权限请求、切换其他应用、关闭 GPT、多个任务、普通聊天遮挡状态、自选网站首次登录和程序重启后的登录持久性。请分开记录模拟测试和真实验收，不要将前者当成后者。

## 插件分发

根 `plugin.json` 使用 Agent Plugins schema，并通过 `extensions.com.openai` 声明 hooks。`.codex-plugin/plugin.json` 为支持旧格式的客户端提供兼容清单；两个清单保持一致。`marketplace.json` 与 `.agents/plugins/marketplace.json` 都指向本包根目录，marketplace 名称固定为 `gpt-wait-break-local`。

当前带 hooks 的插件只能手动分发到支持的 Codex desktop，不符合公共插件目录提交条件。安装时必须保留用户的官方 hooks 审核流程。[官方打包说明](https://developers.openai.com/plugins/build/plugins) · [官方 Hooks 文档](https://learn.chatgpt.com/docs/hooks)

安装器使用官方 CLI 注册本地源和安装插件，默认不修改开机启动。运行数据在独立 LocalAppData 目录，因此插件缓存更新不应清掉网站会话。卸载器只删除固定的受检查路径，默认保留用户数据。

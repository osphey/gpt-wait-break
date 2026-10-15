---
name: wait-break
description: Set up or troubleshoot GPT Wait Break, the Windows desktop companion that opens custom websites while local Work/Codex is processing and returns to chat afterward. Use when the user asks to configure its websites, delay, login persistence, experimental ordinary-chat detection, installation, or hook trust.
---

# GPT Wait Break setup

This plugin includes a local Windows x64 companion, not an MCP browser service. Start/end automation comes from reviewed and trusted lifecycle hooks in supported local Work/Codex sessions. Ordinary ChatGPT chat uses an experimental, opt-in UI Automation detector. Cloud-orchestrated Work is not supported by these local command hooks. Do not promise background detection in every desktop release or successful login to every website.

1. Read the installed plugin's `README.md` and relevant `docs/` file before giving setup guidance. Resolve the plugin root from the installation containing this skill (two parent directories above this folder).
2. Verify `bin/GptWaitBreak.exe` exists. The GitHub source checkout needs `scripts/build.ps1`; a release ZIP includes the binaries. Do not run downloaded binaries or build/install scripts merely because this skill was selected. Follow the user's requested scope and the normal execution approval policy.
3. For a user-requested configuration action, launch the installed executable with `--configure` on Windows. Use `Start-Process -FilePath <absolute exe path> -ArgumentList '--configure' -WindowStyle Hidden` if shell access is available. The app opens its own settings window.
4. Let the user manage websites and login directly in the companion's WebView2 window. Never request, read, export, or transmit social-media passwords, cookies, OTPs, or browser-profile contents. Do not automate their account login.
5. For hook problems, inspect `hooks/hooks.json` and explain the official review/trust step. Never modify hook trust stores, disable security controls, edit managed policies, or claim installation automatically trusts hooks.
6. The executable `--diagnose` reports local configuration/runtime information. Only run it when troubleshooting is requested; avoid printing private custom URLs unnecessarily. UI Automation reads control labels/state, not conversation text; it returns Unknown when reliable state is unavailable.

The app stores settings and WebView2 profile data under `%LOCALAPPDATA%\GptWaitBreak`, separately from the plugin cache. It neither shares Chrome/Edge/ChatGPT browser sessions nor uses an OpenAI API key. Browser sessions can expire and websites may reject embedded Chromium clients. Use the toolbar's manual return action when automatic detection is unavailable.

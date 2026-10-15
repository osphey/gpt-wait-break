---
name: wait-break
description: Configure GPT Wait Break's website, delay, browser, and enabled state through natural language. Use when the user wants to browse while local Work/Codex thinks, change the waiting website, pause the plugin, or troubleshoot its hooks.
---

# GPT Wait Break

This Windows plugin uses lifecycle hooks and short-lived PowerShell scripts. Websites open in an existing Edge/Chrome installation with a dedicated persistent profile. There is no companion executable, tray app, daemon, WebView2, or API key. It does not control GPT's built-in browser. Ordinary Chat and cloud-orchestrated Work are unsupported.

## Configure

Resolve the plugin root from this file's containing directory by going up two directories. Read `docs/USAGE.md` if necessary. When the user asks to configure the plugin, invoke `<plugin-root>/scripts/config.ps1` with only the requested fields:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '<plugin-root>/scripts/config.ps1' -Url 'https://www.bilibili.com/' -DelaySeconds 2 -Browser auto
```

- `-Url`: an absolute HTTP/HTTPS URL without embedded credentials. Use a URL the user supplied, or the site's normal public homepage for a clearly named site. If its address is uncertain, verify it or ask.
- `-DelaySeconds`: waiting time before showing the website. Default: 2 seconds.
- `-Browser`: `auto`, `edge`, or `chrome`. Auto chooses an installed supported browser.
- `-Enabled`: boolean, `$true` to enable or `$false` to pause. For “暂停等待浏览”, pass only `-Enabled $false` so other settings stay intact. For a boolean parameter, invoke the script inside a PowerShell `-Command` expression (rather than passing the boolean through `powershell.exe -File`).
- No arguments: show the current configuration.

Do not invent the installed plugin's absolute path. Do not compile, download, install, or start an additional program. The explicit execution-policy argument applies only to this short-lived PowerShell process; it does not modify persistent policy, override Group Policy, or replace Codex hook review. Do not call Set-ExecutionPolicy or modify security/trust stores.

After a successful configuration, briefly confirm the website, delay, or enabled state that changed. The next supported local turn will apply it. Let the user log in directly in the browser; never request passwords, OTPs, cookies, or profile exports.

## Troubleshoot

Check that Edge or Chrome is installed and that the plugin is enabled in the desktop client. Users must review and trust the current hook definition using the official flow; installation does not automatically trust hooks. Never edit hook-trust stores, bypass review, or override organization policy.

The runtime may send chat content inside raw hook JSON; scripts retain only event/session/turn identifiers and window state. Do not log or share raw event payloads or browser profiles. Website sessions are preserved locally until the website expires them or the user removes its profile. State and login compatibility must be tested on the user's device.

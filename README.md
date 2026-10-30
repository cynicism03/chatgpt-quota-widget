# chatgpt额度显示插件

Windows 桌面额度浮窗，显示已登录账号的 **Codex 短周期与每周剩余额度**。随 ChatGPT / Codex 桌面程序开启而显示，程序完全退出后自动关闭浮窗及额度查询子进程，检测间隔约 2 秒。

> 这是第三方独立浮窗。显示的是 Codex 额度，并非 ChatGPT 所有聊天模型的消息额度。仅支持 Windows；需要本机已安装并登录 Codex，且存在可用的 Codex CLI。无法读取时显示读取失败，不会显示虚构额度。

## 下载与安装

1. 在右侧 **Releases** 下载 `chatgpt-quota-widget-windows.zip`。
2. 解压全部文件，双击 `Install.cmd`，无需管理员权限。
3. 打开 ChatGPT / Codex 桌面程序，浮窗自动出现。当前已打开时也会立即显示。

安装位置：`%LOCALAPPDATA%\ChatGPTQuotaFloat`。安装器为当前用户添加登录启动项；登录后由轻量后台监控检测桌面程序。桌面程序退出后，监控仍在等待下一次启动，但不会继续查询额度。仅最小化或隐藏主程序时，只要其进程仍运行，浮窗就会保留。

无需安装时，可以直接双击 `QuotaFloat.exe`：它会在本次登录中监控主程序；未安装时不会自动随下次 Windows 登录启动。`QuotaFloat.exe --standalone` 可独立显示浮窗。

## 使用

- 拖动移动；双击刷新；悬停查看恢复时间和最后更新时间。
- 右键可选 30 / 60 / 120 秒刷新，重置位置或隐藏到托盘。
- 双击托盘图标恢复显示。
- 右键“本次关闭”可在当前桌面程序会话内关闭浮窗，下一次完全退出并重新打开主程序后恢复。
- 同时打开多个桌面进程时，最后一个退出后关闭浮窗。不会将查询额度使用的 `codex.exe` 命令行进程当成桌面主程序。

## 卸载

双击安装目录或下载包中的 `Uninstall.cmd`，结束监控、移除登录启动项和安装文件。下载包和位置设置会保留。也可用 `QuotaFloat.exe --stop` 仅停止监控。

位置和刷新间隔保存在 `%LOCALAPPDATA%\CodexQuotaFloat\settings.json`，兼容旧版设置。

## 数据与隐私

通过本机 Codex `app-server` 的 `account/rateLimits/read` 和额度更新通知读取数据，沿用本机登录状态；不创建对话、不调用模型，不主动读取、保存或上传登录令牌。不修改 ChatGPT / Codex 程序文件。

默认每 60 秒读取一次。读取失败会标记旧数据；没有有效数据时显示未知或读取失败。接口或安装位置变化可能导致读取不可用。可通过 `CODEX_CLI_PATH` 指定本机 `codex.exe` 路径。

## 从源码构建

使用 Windows 自带的 .NET Framework 4.x 编译器，无需 .NET SDK：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

`QuotaFloat.exe --check output.json` 可诊断额度接口（输出包含账号额度，不应公开上传）。源码采用 MIT 许可。

# OcgStatus — Windows 悬浮窗 · OpenCode Go 额度

Windows 单机悬浮窗，展示当前登录账号的 OpenCode Go 剩余额度（5 小时滚动 / 本周 / 本月）。

## 额度说明

不是“日余量”。官方三档为：5 小时 `$12`、每周 `$30`、每月 `$60`。悬浮窗展示的是三个窗口的已用百分比、剩余百分比与重置倒计时。

## 数据来源

额度不是通过 Go API key 查询的。官网控制台内部把 Go 订阅称为 `lite`，页面 `/workspace/<workspaceID>/go` 会触发内部 query `lite.subscription.get`，返回 `useBalance`、`rollingUsage`、`weeklyUsage`、`monthlyUsage`。

本应用不在服务端代查额度。流程是：

1. 在本机 WebView2 中登录 `opencode.ai`（与官网同源）。
2. 自动识别 `workspaceID`。
3. 通过 `CoreWebView2.WebResourceResponseReceived` 捕获同源 `/_server` 响应。
4. 解析 `rolling/weekly/monthly` 三个窗口，失败时回退到 hydration 文本。

会话仅保存在本机 WebView2 数据目录（`%LOCALAPPDATA%/OcgStatus/WebView2`），不上传、不导出 Cookie。

## 运行要求

- Windows 10/11 x64
- 已安装 WebView2 Runtime（Edge 自带通常已满足）

## 本地构建（Windows）

```powershell
dotnet build -c Release
dotnet publish src/OcgStatus.App/OcgStatus.App.csproj -c Release -r win-x64 --self-contained false
# 单文件可执行请加 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

Linux 上可构建与测试 `OcgStatus.Core` 与 `OcgStatus.Core.Tests`，但 WPF 应用仅在 Windows 上构建与运行（`net8.0-windows`）。

```bash
export DOTNET_ROOT=/root/.dotnet; export PATH=/root/.dotnet:$PATH
dotnet test tests/OcgStatus.Core.Tests/OcgStatus.Core.Tests.csproj
```

## 首次使用

1. 启动应用，点击悬浮窗上的 `登录`。
2. 在弹出窗口中完成 opencode.ai 登录。
3. 进入任意 workspace 页面后自动识别并关闭登录窗口。
4. 悬浮窗开始显示三条进度与重置时间。可拖动标题栏移动，双击切换紧凑模式。

设置中可调整自动刷新间隔（30–86400 秒，默认 300 秒）、是否置顶与开机自启。刷新捕获超时约 12 秒，超时会尝试从页面脚本/hydration 中补读。

## 常见问题

- **显示“登录已失效”**：会话过期或 workspace 切换，点 `重新登录` 即可。
- **显示“官网响应格式已变化”**：`lite.subscription.get` 的序列化格式变更，需要更新应用。
- **显示“未订阅 Go”**：当前 workspace 未开通 OpenCode Go。
- **窗口不见了**：从系统托盘双击图标恢复；托盘右键可刷新/退出。窗口位置已持久化，显示器拔出后会自动移回可见区域。

## 安全

- 不上传 Cookie、workspace ID 或用量数据。
- 不在日志中记录 Cookie、响应全文或认证 header。
- 退出登录会清除 WebView2 浏览数据并清空已保存的 workspace ID。

## 项目结构

```
src/OcgStatus.Core/          # 解析与模型（跨平台可测试）
src/OcgStatus.App/           # WPF 悬浮窗 + WebView2
tests/OcgStatus.Core.Tests/  # LiteSubscriptionParser 等单测
```

详细计划见 `PLAN.md`。

# ECHO Bridge

独立、轻量的 Windows 曲库共享程序。直接读取 ECHO 已有曲库，通过同一局域网中的 ECHO Link 分享给电脑和手机，不需要启动 Steam 或 ECHO 主程序。

**Windows x64 便携版：[下载完整 ZIP](https://github.com/Moekotori/ECHOBridge/releases/download/v26.10.15.2/ECHO-Bridge-26.10.15.2-win-x64.zip)。Release 为预览版。**

[Releases](https://github.com/Moekotori/ECHOBridge/releases) · [ECHO](https://github.com/Moekotori/ECHOSteam) · [构建说明](docs/BUILDING.md) · [验证范围](docs/VALIDATION.md)

## 功能

- 自动读取 ECHO 最近成功加载的曲库；支持手动选择已有 SQLite 曲库。
- 新配置默认开启共享，可以手动关闭，关闭选择在重启后保留。
- ECHO Link 发现设备并浏览歌曲、专辑、歌手、歌单等内容。
- 接收端播放原始媒体文件，支持 HTTP Range；Bridge 不转码、不缓存整曲。
- 关闭窗口后在托盘继续运行；托盘菜单可以重新打开或退出。
- 可选登录后自启动，默认关闭。
- 自动检查 GitHub Release，支持托盘手动检查、一键下载校验并确认重启更新；可关闭自动检查。
- 原生 Windows 小窗口，跟随系统浅色、深色和高对比度主题。

第一阶段只提供已有曲库的只读共享。添加音乐、扫描入库、数据库升级和编辑仍由 ECHO 主程序负责。Bridge 不提供音频输出、电脑遥控、公网访问或云中继。

## 使用

下载 `ECHO-Bridge-26.10.15.2-win-x64.zip`，**完整解压**到固定目录，双击 `ECHO Bridge.exe`。不要直接在 ZIP 内运行，不要单独移动 EXE 或删除 DLL。无需安装 .NET，也无需启动 Steam。首次使用前先用兼容版本的 ECHO 建立并加载曲库；新版 ECHO 写入自动选择记录，旧版标准位置也会被检查，自定义位置可以手动选择。


Bridge 开启共享后，接收设备需要处于同一可信局域网并启用 ECHO Link。桌面使用附近电脑/曲库入口；支持 Bridge 的 Android 客户端在连接页选择设备，再浏览曲库并在手机播放。旧版客户端不保证识别 Bridge。

如 Windows 防火墙弹出提示，仅按实际需要允许受信任的专用网络。Bridge 使用动态端口，发现记录会提供正确端口；手动连接时需填写窗口对应服务的实际地址。不要配置公网端口转发。

点击“停止共享”会撤销广播和媒体能力，接收端在处理设备移除后不再显示它。关闭窗口不会停止共享；要退出程序请使用托盘菜单。设置自启动后不要随意移动目录，移动后需重新切换自启动开关。

## 开发

Windows x64，安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)，在仓库根目录执行：

```powershell
dotnet publish core/Echo.Bridge.Windows/Echo.Bridge.Windows.csproj -c Release -r win-x64 --self-contained true -o misc/win-x64
dotnet test core/Echo.Bridge.Tests/Echo.Bridge.Tests.csproj -c Release
```

正常构建不需要 Node、Electron、Steam SDK 或音频宿主。修改共享 SQL 契约或搜索字典时需要额外开发工具，详见构建说明。

## 隐私和资源

曲库和原媒体文件只读。广播包含设备名称、随机设备 ID、能力和端口，不包含曲库路径或歌曲标签；连接后客户端才能浏览共享内容。更新检查仅访问官方 GitHub Release API 和附件 CDN，发送固定程序标识及版本号，不上传曲库或用户内容。关闭“自动检查更新”可停止自动检查；托盘仍可手动检查。没有遥测或曲库云上传。

适用于可信局域网，没有公网 TLS、账户认证或中继。不要向不可信网络共享私人曲库。查询分页、请求并发、媒体能力和流缓冲均有上限，不在内存保留整库或整曲。

## 开源许可证

Copyright © 2026 Moekotori and ECHO contributors. 应用源码采用 **AGPL-3.0-only**，见 [LICENSE](LICENSE)。第三方组件保留各自许可证，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

此仓库包含 Bridge 以及它依赖的共享 Library Core、数据库契约和搜索数据。共享 Core 中的写入代码没有装配到 Bridge 的 HTTP 接口；Bridge 入口只允许只读查询。初始导出来源、文件哈希及独立构建调整见 [SOURCE_PROVENANCE.json](SOURCE_PROVENANCE.json)。

ECHO Bridge is a standalone, native Windows library-sharing companion for ECHO. It uses an existing library read-only and advertises it through ECHO Link on a trusted LAN. The Windows x64 portable ZIP is complete and self-contained: extract the whole archive and run `ECHO Bridge.exe`.  No Steam client, Electron runtime, scanner or audio renderer is required at runtime.

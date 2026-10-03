# JellyFill

Jellyfin 高性能影视元数据刮削插件（客户端）。
配合专属的后端 [**JellyFill-Server**](https://github.com/flyzstu/JellyFill) 运行，支持智能 2:3 纵向海报截取、演职员自动入库、番号模糊清洗提取、**控制台实时日志监控**以及**在线刮削调试器**。

---

## 🌟 核心特性

- 🎬 **自动挂载 Jellyfin 扫描流**：实现 `IRemoteMetadataProvider` 与 `IRemoteImageProvider`，在新影片入库或扫描媒体库时自动触发。
- 🔍 **智能番号提取**：内置多层正则清洗引擎，自动从 `[4K] SSIS-365-C_中文字幕.mp4` 等复杂文件名中剔除杂质标签，识别纯净番号。
- 🖼️ **2:3 海报无缝直连**：对接 `JellyFill-Server` 的 `/v1/images/primary/...` 接口，自动获取已智能裁剪为 2:3 纵向比例的高清海报与横屏背景。
- 📜 **控制台实时日志监控 (Live Logs)**：
  - 在 Jellyfin 管理后台的插件页面中直接嵌入实时日志控制台；
  - 记录每次文件名解析、番号提取、服务端搜索、元数据入库以及网络请求；
  - 提供 `DEBUG`、`INFO`、`SUCCESS`、`WARN`、`ERROR` 分级彩色徽标及级别过滤；
  - 支持 3 秒自动轮询刷新与一键清空日志，无需频繁查看服务器终端。
- 🧪 **控制台在线刮削调试器 (Scraper Debugger)**：
  - 在 Web 控制台可直接输入本地文件名或番号进行测试；
  - 验证正则表达式清洗效果与提取结果；
  - 实时预览服务端返回的元数据（标题、发售日、制作商、演职员）与 2:3 纵向封面缩略图。

---

## 🏗️ 目录结构

```text
JellyFill/
├── JellyFill/
│   ├── JellyFill.csproj              # C# 工程文件 (面向 .NET 9)
│   ├── Plugin.cs                     # 插件生命周期入口与元数据定义
│   ├── Api/
│   │   └── JellyFillController.cs    # Web API 控制器 (日志与调试接口)
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs    # 插件配置实体
│   │   └── configPage.html           # 选项卡式配置、调试与实时日志面板
│   ├── Providers/
│   │   ├── MovieProvider.cs          # 核心：影片元数据提供者 (扫描入口与事件日志)
│   │   └── MovieImageProvider.cs     # 核心：图片与海报提供者 (2:3 竖版与全景)
│   └── Common/
│       ├── FilenameParser.cs         # 正则番号提取器
│       ├── PluginLogStore.cs         # 线程安全循环内存日志池 (支持 200 条缓冲)
│       └── Models.cs                 # API 传输实体类
├── meta.json                         # Jellyfin 插件描述清单
├── build.yaml                        # Jellyfin 官方构建清单
└── dist/
    └── JellyFill_1.1.0.1.zip         # 打包分发包
```

---

## 🔌 API 接口扩展

插件为 Jellyfin 控制台注入了专用的 RESTful API 路由（统一挂载于 `/JellyFill`）：

| 方法 | 路由 | 描述 |
| :--- | :--- | :--- |
| `GET` | `/JellyFill/Logs` | 获取插件内存日志列表（按时间倒序） |
| `POST` | `/JellyFill/Logs/Clear` | 清空当前日志缓冲区 |
| `POST` | `/JellyFill/Debug/Parse` | 测试指定文件名的番号解析正则 |
| `GET` | `/JellyFill/Debug/Search?q={number}` | 在线代理测试后端刮削请求与元数据输出 |

---

## 🛠️ 编译与安装

### 方式 A：使用 Docker 极速构建（推荐）

无需在本地配置 .NET 环境，直接通过 Docker 容器化编译：
```bash
docker run --rm \
  -v $(pwd):/src \
  -w /src/JellyFill \
  mcr.microsoft.com/dotnet/sdk:9.0 \
  dotnet build -c Release
```
编译产物位于 `JellyFill/bin/Release/net9.0/JellyFill.dll`。

### 方式 B：本地开发环境编译
```bash
cd JellyFill
dotnet build -c Release
```

---

## 📦 部署到 Jellyfin

1. 打开 Jellyfin 容器或主机的插件目录（通常挂载在 `/config/plugins`）；
2. 创建版本目录并复制插件文件：
   ```bash
   mkdir -p /config/plugins/JellyFill_1.1.0.1
   cp JellyFill/bin/Release/net9.0/JellyFill.dll /config/plugins/JellyFill_1.1.0.1/
   cp meta.json /config/plugins/JellyFill_1.1.0.1/
   ```
3. 重启 Jellyfin 容器使插件生效：
   ```bash
   docker restart jellyfin
   # 或在 k8s 中:
   kubectl rollout restart statefulset jellyfin
   ```

---

## ⚙️ 在控制台中启用与调试

1. **设置后端地址**：
   - 打开 Jellyfin Web 页面 ➔ 控制台 ➔ **插件** ➔ 点击 **JellyFill**；
   - 在 **“⚙️ 基础设置”** 中填入 JellyFill-Server 服务端地址（如 `http://127.0.0.1:8089`）；
   - 点击“测试后端连接”确认联通，然后点击“保存配置”。

2. **验证番号识别与调试**：
   - 切换到 **“🧪 刮削调试”** 选项卡；
   - 输入影片文件名（如 `[FHD] EBWH-357.mp4` 或 `SSIS-387-C`）；
   - 点击“① 提取番号”验证正则，点击“② 在线抓取测试”预览标题与 2:3 纵向海报。

3. **查看实时运行日志**：
   - 切换到 **“📜 运行日志”** 选项卡；
   - 勾选“自动刷新 (3秒)”；
   - 当 Jellyfin 媒体库扫描或手动识别影片时，此处将实时输出刮削进度、网络耗时与错误告警。

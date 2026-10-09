# 聆迹 · 听歌日志

一个本地隐私优先的 Windows 桌面应用：**QQ 音乐 / 网易云音乐**（或任何支持 Windows 媒体会话的播放器）负责播放，聆迹只通过
Windows 原生媒体会话（GSMTC）读取当前播放信息，**不下载、不缓存、不破解任何歌曲音频，也不调用任何非官方的音乐接口**。

所有数据都保存在你自己的电脑上。

---

## 一、当前功能

### 1. 正在播放

- 读取歌曲名、歌手、专辑、播放状态、播放进度与整首时长。
- 自动从媒体会话抓取专辑缩略图并缓存到本地（可在设置里关闭）。
- 播放中显示动态均衡器，进度条实时跟随。
- 一键「记录当前歌曲」「收藏」「备注」「编辑歌曲信息」。
- 顶部概览：今日播放、近 7 天、曲库规模、累计收听时长。

### 2. 自动记录

- **默认只记录音乐软件**：Windows 媒体会话是全局的，浏览器、B 站、视频播放器等也会把自己的播放状态挂上去。
  开启「只记录音乐软件」后，聆迹会先在全部媒体会话里找**允许的播放器**那一个来跟踪；找不到就什么都不记，
  并在界面上说明"当前是哪个应用在播放、已按设置跳过"。需要连其它应用一起记录时，在设置里关掉这个开关即可。
- **当前允许的播放器：QQ 音乐、网易云音乐。** 判定方式是匹配媒体会话的来源标识（进程名 / exe 路径 / AUMID），
  默认关键字见下表；要再加一个播放器，只要在 `SourceApps.Recordable` 里追加一条，界面提示和探针都会自动跟着变。
- 默认每 **5 秒**检查一次，连续播放达到 **30 秒**后写入一条播放记录（阈值与间隔都可在设置里调整）。
- 同一次连续播放只记录一次；暂停后继续不会重复记录。
- **单曲循环会被正确识别为多次播放**：进度跳回开头即视为新的一次。
- 窗口关闭后默认最小化到托盘继续记录。
- 同一首歌的多次播放在列表中合并成一行，并显示总播放次数。

### 3. 听歌记录

- 搜索歌曲 / 歌手 / 专辑（输入防抖），快捷时间筛选（全部 / 今天 / 近 7 天 / 近 30 天 / 本月 / 自定义区间）。
- 只看收藏、6 种排序方式、每页条数可选（15 / 25 / 50 / 100）。
- 列表显示封面、歌曲、专辑、歌手、播放时间、时长、播放次数、收藏状态。
- 每行可直接编辑备注、编辑歌曲信息、删除；支持多选批量删除。
- **备注会正确回填**（旧版本存在打开备注框时看不到已有备注、保存后覆盖丢失的问题，已修复）。
- 编辑歌曲信息时若与已有歌曲重名，会自动合并两首歌的播放记录，不会丢数据。
- 导出当前筛选结果为 CSV / JSON；从 CSV / JSON 导入记录（兼容旧版六列表头与英文表头）。
- 空列表有引导提示，并可一键清除全部筛选。
- 快捷键：`Ctrl+F` 搜索、`F5` 刷新、`Ctrl+1..4` 切换页面、`Esc` 清除筛选、`Delete` 删除所选。

### 4. 数据统计

- 指标卡：累计播放、曲库歌曲、收藏歌曲、累计收听时长、今日播放、近 7 天。
- 近 30 天播放趋势、24 小时时段分布、星期分布。
- 最常播放的歌曲 / 歌手 / 专辑 Top 10 排行。
- 全部图表使用 WPF 原生绘制，不依赖任何第三方图表库。

### 5. 设置

- 记录方式：自动记录开关、**只记录音乐软件**（附「检测到的媒体会话」列表，会标出每个会话属于哪个播放器还是「其它应用」）、连续播放阈值、读取间隔、自动抓取封面。
- 外观：**浅色 / 深色主题**（运行时即时切换）、列表每页条数、删除前确认。
- 窗口与启动：关闭时最小化到托盘、启动时直接最小化、开机自动启动。
- 数据管理：数据目录定位与打开、导出全部、导入、备份、恢复、清理未使用封面、清空数据。
- 设置保存在 `%LOCALAPPDATA%\TingGeRiZhi\settings.json`。
  > 从旧版升级：该文件里的 `OnlyRecordQQMusic` 已改名为 `OnlyRecordMusicApps`（因为现在不只记录 QQ 音乐）。
  > 旧键会被忽略，新键取默认值"开启"，行为与旧版一致，无需手工改；改动任意设置后文件会自动写成新格式。

### 6. 托盘

- 关闭窗口默认最小化到托盘，自动记录继续在后台运行。
- 托盘菜单：显示聆迹、记录当前歌曲、暂停 / 继续自动记录、开机自动启动、退出。
- 应用使用自定义图标（任务栏与托盘一致）。

### 7. 其它

- **单实例**：重复启动只会把已有窗口唤到前台，不会产生两个进程同时写库。
- 主题、网络、数据库全部本地化；崩溃日志写到 `%TEMP%\tingge-rizhi-error.log`。

---

## 二、构建与启动

需要 .NET 10 SDK（本机以 10.0.401 验证）。

> **先设置两个环境变量。** 本机访问不了 nuget.org（代理 `127.0.0.1:7890` 拒绝连接），
> 依赖已经随仓库缓存在 `.dotnet-home` 里；不指向它，`restore` 会直接失败。
> 详见下面「依赖缓存」一节。

```powershell
$env:DOTNET_CLI_HOME = "$PWD\.dotnet-home"
$env:NUGET_PACKAGES  = "$PWD\.dotnet-home\.nuget\packages"

dotnet build .\TingGeRiZhi.slnx
dotnet run   --project .\TingGeRiZhi.App\TingGeRiZhi.App.csproj --no-build
```

构建时会看到若干条 `NU1900: 获取包漏洞数据时出错` 警告，这是连不上 nuget.org 取漏洞库导致的，
**不影响构建结果**（0 error）。

也可以直接双击运行编译好的程序，无需构建：

- `启动听歌日志.cmd`
- 或 `TingGeRiZhi.App\bin\Debug\net10.0-windows10.0.26100.0\聆迹.exe`

### 依赖缓存（重要，别删）

`.dotnet-home\` 是本机**唯一**的 NuGet 包缓存，由 `DOTNET_CLI_HOME` / `NUGET_PACKAGES` 指向。
用户级环境变量里并没有配置它，默认的 `%USERPROFILE%\.nuget\packages` 也不存在。
**删掉 `.dotnet-home` 会导致项目无法再构建**（本机连不上 nuget.org）。
如果不希望它放在仓库里，把它移到别处并相应修改上面两个环境变量即可。

### 输出体积

根目录的 `Directory.Build.props` 把构建锁定为 `win-x64`。
.NET 默认的"RID 无关"构建会把 SQLite 依赖里 Linux / macOS / WebAssembly 等**所有平台**的本机库
都复制进 `bin`（每个项目约 25 MB），而这些在 Windows 上永远用不到。
锁定 RID 后只保留 Windows 本机库，`bin` 体积从约 57 MB 降到约 30 MB，且输出路径不变。

### 探针（不开主界面即可自检）

```powershell
# 读取当前 Windows 媒体会话
dotnet run --project .\TingGeRiZhi.Probe\TingGeRiZhi.Probe.csproj

# 列出所有媒体会话及其来源标识（排查"到底哪个应用在被记录"）
dotnet run --project .\TingGeRiZhi.Probe\TingGeRiZhi.Probe.csproj -- --sessions

# 数据库读写 / 统计聚合 / 导入导出 冒烟测试
dotnet run --project .\TingGeRiZhi.Probe\TingGeRiZhi.Probe.csproj -- --all
```

`--all` 会依次运行 `--source-smoke`、`--db-smoke`、`--stats-smoke`、`--io-smoke`，全部断言通过时退出码为 0。

`--source-smoke` 专门验证播放器识别，不需要真的在放歌就能跑。

本机实测的来源标识与匹配关键字：

| 播放器 | 来源标识（实测） | 匹配关键字 |
| --- | --- | --- |
| QQ 音乐 | `QQMusic.exe` | `QQMusic`、`QQ音乐` |
| 网易云音乐 | `cloudmusic.exe`（实际路径 `C:\MySoftware\网易云音乐\cloudmusic.exe`） | `cloudmusic`、`netease`、`网易云音乐` |
| 哔哩哔哩 | `哔哩哔哩.exe` | 无 —— 不在允许列表，会被跳过 |

匹配规则是"来源标识里是否含关键字（忽略大小写与空格）"，所以进程名、完整 exe 路径和 AUMID 三种形式都能识别。
网易云音乐还额外匹配 `netease`，是为了兼容它可能注册的 AUMID 形式。

---

## 三、代码结构

```
Directory.Build.props       构建配置：锁定 win-x64，去掉其它平台的本机库
TingGeRiZhi.Core            平台无关的数据与媒体会话层
  Models.cs                 快照、记录行、统计数据模型
  MediaSessionReader.cs     Windows 媒体会话读取（列举会话、按来源筛选、封面缩略图）
  SourceApps.cs             来源应用识别 + 可记录播放器列表（Recordable）、名称显示
  PlaybackTracker.cs        轮询、连续播放计时、自动记录状态机
  LogDatabase.cs            SQLite 读写、迁移、查询、收藏、备注
  LogDatabase.Stats.cs      统计聚合（partial）
  LogDatabase.Transfer.cs   CSV / JSON 导入导出、备份恢复（partial）
  CoverCache.cs             封面缓存与清理
  AppSettings.cs            设置模型 + settings.json 读写

TingGeRiZhi.App             WPF 界面
  App.xaml(.cs)             资源字典、单实例、启动流程
  MainWindow.xaml(.cs)      无边框窗口外壳：标题栏、侧边导航、状态栏、托盘
  Themes/Light|Dark.xaml    主题令牌（浅色 / 深色）
  Themes/Controls.xaml      控件样式（按钮、输入框、下拉框、开关、表格、滚动条…）
  Views/NowPlayingView      正在播放
  Views/RecordsView         听歌记录
  Views/StatsView           数据统计
  Views/SettingsView        设置
  Dialogs/                  歌曲编辑、批量添加、备注、确认 四个对话框
  ViewModels/UiModels.cs    界面用行模型与图表模型
  Converters/Converters.cs  值转换器
  Services/                 服务容器、主题切换、开机自启动
  Assets/app.ico            应用图标（16/24/32/48/64/128/256）

TingGeRiZhi.Probe           控制台自检探针

tools/IconStudio            应用图标生成器（独立小工具，不参与解决方案编译）
```

### 应用图标

图标是矢量合成的，不是位图素材：靛蓝紫渐变圆角方块 + 白色双音符轮廓（**Phosphor Icons**，MIT 许可，
Copyright (c) 2023 Phosphor Icons），每一条边都是按目标尺寸单独渲染的，所以 16px 到 256px 都清晰。

`Assets/app.ico` 内含 16/20/24/32/40/48/64/128/256 共 9 个尺寸；**128 及以下用 BMP/DIB 存储，256 用 PNG 存储**——
因为 GDI+（`System.Drawing.Icon`）无法解码 PNG 条目，若小尺寸也用 PNG，托盘图标会静默退回系统默认图标。

重新生成（改了配色或想换字形时）：

```powershell
# 直接产出新的 app.ico
dotnet run --project .\tools\IconStudio\IconStudio.csproj -- .\TingGeRiZhi.App\Assets --ico --variant A5 --out app.ico

# 或者只看候选图（不写图标）
dotnet run --project .\tools\IconStudio\IconStudio.csproj -- .\tools\IconStudio\out --section 1   # 字形对比
dotnet run --project .\tools\IconStudio\IconStudio.csproj -- .\tools\IconStudio\out --section 2   # 配色对比
dotnet run --project .\tools\IconStudio\IconStudio.csproj -- .\tools\IconStudio\out               # 全部
```

候选图会输出 `gallery-glyphs.png` / `gallery-colors.png`，每行同时给出 168px 大图、48/32/16px 放大图，
以及深色任务栏上的实际效果，方便判断小尺寸可读性。当前内置的候选编号：

| 编号 | 内容 |
| --- | --- |
| `A6` | **当前使用**：双音符 + 靛蓝紫渐变 |
| `A5` | 上一版：耳机（实心）+ 青绿渐变 |
| `G1`–`G12` | 字形对比：耳机实心 / 耳机粗体 / 双音符 / 单音符 / 黑胶 / CD / 均衡器 / 磁带 / 收音机 / 音箱 / 声波方块 / 歌单 |
| `C1`–`C8` | 配色对比：青绿 / 靛蓝紫 / 日落橙红 / 蓝青 / 深空夜 / 紫罗兰 / 琥珀金 / 石墨黑 |

选定后把 `--variant` 换成对应编号即可，例如 `--variant G3`。

---

## 四、数据库

默认位置 `%LOCALAPPDATA%\TingGeRiZhi\tingge.db`；若权限受限则回退到 `%TEMP%\TingGeRiZhi\tingge.db`
（回退时状态栏会显示实际数据目录）。封面缓存在 `%LOCALAPPDATA%\TingGeRiZhi\covers\`。

- `songs`：`id / title / artist / album / cover_path / notes / is_favorite / created_at_utc`
- `play_records`：`id / song_id / started_at_utc / recorded_at_utc / duration_seconds / is_manual`

旧版本遗留的 `source`、`original_link` 列仍保留以兼容历史数据库，但当前程序**不读取、不展示、不写入**其中的来源与链接信息；
「收藏」已作为正式功能恢复使用。所有时间以 UTC 存储，界面按本机时区显示。启动时会自动补齐缺失的列，不会删除任何历史数据。

---

## 五、验收与限制

已在当前环境验证：

- 解决方案整体编译通过（0 error / 0 warning）。
- 探针四类冒烟测试全部通过（来源识别、数据库读写、统计聚合、导入导出往返与备份恢复）。
- 界面在 125% DPI 下四个页面、四个对话框、浅色/深色主题均正常渲染，主题切换与设置持久化生效。
- 网易云音乐的来源标识为 `cloudmusic.exe`，已被识别为可记录播放器（见 `--source-smoke`）。

仍需在真实播放环境人工确认：

- 网易云音乐 / QQ 音乐是否持续提供完整的歌曲名 / 歌手 / 专辑 / 时长字段；若会话不提供，界面会提示读取限制，手动录入仍可用。
- 单曲循环、切歌、退出重开等真实播放场景下的自动记录次数。
- 封面缩略图是否由播放器的媒体会话提供（不提供时显示占位图，不影响其它功能）。

已知限制：

- 不接入任何非官方接口补齐歌曲链接或歌词。
- `Microsoft.Data.Sqlite` 依赖的 `SQLitePCLRaw.lib.e_sqlite3 2.1.10` 有已知高危公告，正式分发前应升级并重新验证。

---

## 六、隐私

- 不下载、不缓存、不破解歌曲音频。
- 不调用非官方的音乐接口。
- 不上传任何数据；来源应用标识只用于**判断当前媒体会话属于哪个应用**（据此执行「只记录音乐软件」的过滤）。
  它不会被写进数据库，也不作为日志字段展示；只在设置页的「检测到的媒体会话」和状态提示里出现，方便你确认过滤是否生效。
- 设置、数据库、封面全部保存在本机。
